using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Provider;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using CodeOwls.PowerShell.Paths.Processors;
using CodeOwls.PowerShell.Provider;
using CodeOwls.PowerShell.Provider.PathNodeProcessors;
using CodeOwls.PowerShell.Provider.PathNodes;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Framework.Client;
using Microsoft.TeamFoundation.Framework.Common;

namespace CodeOwls.TFS
{
    [CmdletProvider("TFS", ProviderCapabilities.ShouldProcess | ProviderCapabilities.Credentials)]
    public class TFSProvider : Provider
    {
        protected override IPathResolver PathResolver
        {
            get { return new TFSPathResolver(); }
        }

        protected override System.Management.Automation.PSDriveInfo NewDrive(System.Management.Automation.PSDriveInfo drive)
        {
            drive = new TFSDrive(drive);
            return base.NewDrive(drive);
        }
    }

    public static class Extensions
    {
        public static string Encode(this string str)
        {
            return Uri.EscapeDataString(str);
        }

        public static string Decode(this string str)
        {
            return Uri.UnescapeDataString(str);
        }
    }

    public class TFSDrive : Drive
    {
        public TFSDrive(PSDriveInfo drive) : base( new PSDriveInfo( drive.Name, drive.Provider, "["+drive.Root.Encode()+"]", drive.Description, drive.Credential))
        {            
        }
        

    }

    public class TFSPathResolver : PathResolverBase
    {
        static readonly Regex UriPattern = new Regex(@"^.*\[(.+?)\]\\");

        public override IEnumerable<IPathNode> ResolvePath(IProviderContext context, string path)
        {
            var match = UriPattern.Match(path);
            if (! match.Success)
            {
                throw new InvalidOperationException("path root value is not valid");
            }

            var uri = match.Groups[1].Value;
            uri = uri.Decode();
            ServerUri = new Uri(uri);
            
            var creds = String.IsNullOrEmpty(context.Credential.UserName ) ? context.Drive.Credential : context.Credential;
            if (null != creds)
            {
                Credential = creds.GetNetworkCredential();
            }

            path = UriPattern.Replace(path, String.Empty);

            return base.ResolvePath(context, path);
        }

        protected override IPathNode Root
        {
            get { return new ConfigurationServerNode(ServerUri, Credential); }
        }

        private ICredentials Credential { get; set; }
        private Uri ServerUri { get; set; }
    }

    public class ConfigurationServerNode : PathNodeBase
    {
        private readonly Uri _serverUri;
        private TfsConfigurationServer _server;
        private readonly ICredentials _credentials;

        public ConfigurationServerNode(Uri serverUri, ICredentials credentials)
        {
            _serverUri = serverUri;
            _credentials = credentials;
        }

        TfsConfigurationServer Server
        {
            get
            {
                if (null == _server)
                {
                    _server = new TfsConfigurationServer(_serverUri, _credentials );
                }
                return _server;
            }
        }

        public override IEnumerable<IPathNode> GetNodeChildren(IProviderContext providerContext)
        {
            var nodes = new List<IPathNode>();

            var registry = Server.GetService<ITeamFoundationRegistry>();
            var collection = registry.ReadEntries("/**");
            var re = new Regex(@"\/[^\/]+$");
            var entries = from entry in collection
                         let key = re.Replace(entry.Path, String.Empty) 
                         group entry by key;

            var registryNodes = CollateGroups(entries);

            nodes.AddRange(registryNodes.Children);
            //AddProjectCollectionNodes(nodes);


            return nodes;
        }

        private RegistryEntryCollectionPathNode CollateGroups(IEnumerable<IGrouping<string, RegistryEntry>> entries)
        {
            var rootNodes = new RegistryEntryCollectionPathNode("");
            
            foreach (var entry in entries)
            {
                var segments = entry.Key.Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries);

                var nodes = rootNodes.Children;
                foreach (var segment in segments)
                {
                    var node = nodes.FirstOrDefault(n => n.Name == segment);
                    if (null == node)
                    {
                        node = new RegistryEntryCollectionPathNode( segment );
                        nodes.Add( node );
                    }
                    nodes = ((RegistryEntryCollectionPathNode) node).Children;
                }

                nodes.AddRange( from e in entry select new RegistryEntryPathNode(e) );

            }
            return rootNodes;
        }

        private void AddProjectCollectionNodes(List<IPathNode> nodes)
        {
            var catalogNode = Server.CatalogNode;
            var catalogNodes = catalogNode.QueryChildren(new Guid[] {CatalogResourceTypes.ProjectCollection}, false,
                                                         CatalogQueryOptions.None);


            nodes.AddRange(catalogNodes.ToList().ConvertAll(c =>
                                                                {
                                                                    var n =
                                                                        Server.GetTeamProjectCollection(
                                                                            new Guid(c.Resource.Properties["InstanceId"]));
                                                                    return new TeamProjectCollectionPathNode(n, c);
                                                                }));
        }

        public override IPathValue GetNodeValue()
        {
            return new ContainerPathValue( Server, Name );
        }

        public override string Name
        {
            get { return Server.Name; }
        }
    }

    public class RegistryEntryCollectionPathNode : PathNodeBase
    {
        private readonly List<IPathNode> _children;

        private readonly string _name;

        public RegistryEntryCollectionPathNode(string key)
        {
            _name = key;
            _children = new List<IPathNode>();
        }

        public override IPathValue GetNodeValue()
        {
            return new ContainerPathValue( _name, Name);
        }

        public override IEnumerable<IPathNode> GetNodeChildren(IProviderContext providerContext)
        {
            return Children;
        }

        public override string Name
        {
            get { return _name; }
        }

        internal List<IPathNode> Children
        {
            get { return _children; }
        }
    }

    public class RegistryEntryPathNode : PathNodeBase
    {
        private readonly RegistryEntry _entry;

        public RegistryEntryPathNode(RegistryEntry entry)
        {
            _entry = entry;
        }

        public override IPathValue GetNodeValue()
        {
            return new LeafPathValue( _entry, Name );
        }

        public override string Name
        {
            get { return _entry.Path; }
        }
    }


    public class TeamProjectCollectionPathNode : CatalogNodePathNode
    {
        private readonly TfsTeamProjectCollection _collection;
        
        public TeamProjectCollectionPathNode(TfsTeamProjectCollection collection, CatalogNode node) : base(node)
        {
            _collection = collection;
        }

        public override IPathValue GetNodeValue()
        {
            return new ContainerPathValue( _collection, Name);
        }        
    }

    public class CatalogNodePathNode : PathNodeBase
    {
        private readonly CatalogNode _input;

        public CatalogNodePathNode(CatalogNode input)
        {
            _input = input;
        }

        public override IPathValue GetNodeValue()
        {
            return new LeafPathValue( _input, Name );
        }

        public override string Name
        {
            get { return _input.Resource.DisplayName; }
        }
    }
}
