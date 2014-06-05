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

namespace CodeOwls.TFS
{
    [CmdletProvider("TFS", ProviderCapabilities.ShouldProcess)]
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

            path = UriPattern.Replace(path, String.Empty);

            return base.ResolvePath(context, path);
        }

        protected override IPathNode Root
        {
            get { return new ConfigurationServerNode(ServerUri); }
        }

        private Uri ServerUri { get; set; }
    }

    public class ConfigurationServerNode : PathNodeBase
    {
        private readonly Uri _serverUri;
        private TfsConfigurationServer _server;
        private ICredentials _credentials;

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
        public override IPathValue GetNodeValue()
        {
            return new ContainerPathValue( Server, Name );
        }

        public override string Name
        {
            get { return Server.Name; }
        }
    }
}
