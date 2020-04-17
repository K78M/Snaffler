﻿using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.DirectoryServices.ActiveDirectory;
using System.ServiceModel.Channels;
using SnaffCore.Concurrency;
using static SnaffCore.Config.Options;

namespace SnaffCore.ComputerFind
{
    public class ActiveDirectory
    {
        private List<string> _domainComputers;
        private BlockingMq Mq { get; set; }

        public List<string> DomainComputers =>
            // only compute this once
            _domainComputers
            ?? (_domainComputers = GetDomainComputers());

        private DirectoryContext DirectoryContext { get; set; }
        private List<string> DomainControllers { get; set; } = new List<string>();

        public ActiveDirectory()
        {
            Mq = BlockingMq.GetMq();

            // setup the necessary vars
            if (MyOptions.TargetDomain == null && MyOptions.TargetDc == null)
            {
                try
                {
                    DirectoryContext =
                        new DirectoryContext(DirectoryContextType.Domain, Domain.GetCurrentDomain().Name);
                }
                catch (Exception e)
                {
                    Mq.Error(
                        "Problem figuring out DirectoryContext, you might need to define manually with -d and/or -c.");
                    Mq.Degub(e.ToString());
                    Mq.Terminate();
                }
            }
            else if (!String.IsNullOrEmpty(MyOptions.TargetDc))
            {
                DirectoryContext = new DirectoryContext(DirectoryContextType.Domain, MyOptions.TargetDc);
            }
            else if (!String.IsNullOrEmpty(MyOptions.TargetDomain))
            {
                DirectoryContext = new DirectoryContext(DirectoryContextType.Domain, MyOptions.TargetDomain);
            }
        }

        private void GetDomainControllers()
        {
            try
            {
                var dcCollection = DomainController.FindAll(DirectoryContext);
                foreach (DomainController dc in dcCollection)
                {
                    DomainControllers.Add(dc.IPAddress);
                }
            }
            catch (Exception e)
            {
                Mq.Error(
                    "Something went wrong trying to find domain controllers. Try defining manually with -c?");
                Mq.Degub(e.ToString());
                Mq.Terminate();
            }
        }


        private List<string> GetDomainComputers()
        {
            if (!String.IsNullOrEmpty(MyOptions.TargetDc))
            {
                DomainControllers.Add(MyOptions.TargetDc);
            }
            else
            {
                GetDomainControllers();
            }

            var domainComputers = new List<string>();
            // we do this so if the first one fails we keep trying til we find a DC we can talk to.
            foreach (var domainController in DomainControllers)
            {
                try
                {
                    // TODO add support for user defined creds here.

                    using (var entry = new DirectoryEntry("LDAP://" + domainController))
                    {
                        using (var mySearcher = new DirectorySearcher(entry))
                        {
                            mySearcher.Filter = ("(objectClass=computer)");

                            // No size limit, reads all objects
                            mySearcher.SizeLimit = 0;

                            // Read data in pages of 250 objects. Make sure this value is below the limit configured in your AD domain (if there is a limit)
                            mySearcher.PageSize = 250;

                            // Let searcher know which properties are going to be used, and only load those
                            mySearcher.PropertiesToLoad.Add("name");
                            mySearcher.PropertiesToLoad.Add("dNSHostName");
                            mySearcher.PropertiesToLoad.Add("lastLogonTimeStamp");

                            foreach (SearchResult resEnt in mySearcher.FindAll())
                            {
                                // TODO figure out how to compare timestamp
                                //if (resEnt.Properties["lastLogonTimeStamp"])
                                //{
                                //    continue;
                                //}
                                // Note: Properties can contain multiple values.
                                if (resEnt.Properties["dNSHostName"].Count > 0)
                                {
                                    var computerName = (string) resEnt.Properties["dNSHostName"][0];
                                    domainComputers.Add(computerName);
                                }
                            }
                        }
                    }

                    return domainComputers;
                }
                catch (Exception e)
                {
                    Mq.Trace(e.ToString());
                    throw;
                }
            }

            return null;
        }
    }
}