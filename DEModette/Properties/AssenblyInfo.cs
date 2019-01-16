using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TecWare.DE.Odette;
using TecWare.DE.Server.Configuration;

[assembly: ComVisible(false)]

[assembly: DEConfigurationSchema(typeof(OdetteFileTransferProtocolItem), "DEModette.xsd")]
[assembly: Guid("72eed527-236e-4124-b360-54b25f0f9105")]

[assembly: InternalsVisibleTo("ServerTests")]
