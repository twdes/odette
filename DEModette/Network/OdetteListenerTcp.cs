using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TecWare.DE.Server;
using TecWare.DE.Stuff;

namespace TecWare.DE.Odette.Network
{
	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	internal sealed class OdetteListenerTcpItem : DEConfigItem
	{
		private IServerTcp serverTcp;

		private IListenerTcp currentListener = null;
		private X509Certificate2 serverCertificate = null;

		#region -- Ctor/Dtor --------------------------------------------------------------

		public OdetteListenerTcpItem(IServiceProvider sp, string name)
			: base(sp, name)
		{
		} // ctor

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				Procs.FreeAndNil(ref currentListener);

			base.Dispose(disposing);
		} // proc Dispose

		protected override void OnBeginReadConfiguration(IDEConfigLoading config)
		{
			base.OnBeginReadConfiguration(config);

			// is there the tcp listener
			serverTcp = this.GetService<IServerTcp>(true);

			var useSsl = Config.GetAttribute("ssl", String.Empty);
			if (String.IsNullOrEmpty(useSsl))
				serverCertificate = null;
			else
			{
				Log.Info("Try to locate ssl: {0}", useSsl);
				serverCertificate = ProcsDE.FindCertificate(useSsl).FirstOrDefault(); // todo: select server certificate
				if (serverCertificate == null)
					throw new ArgumentException("Server certificate not found.");
			}
		} // proc OnBeginReadConfiguration

		protected override void OnEndReadConfiguration(IDEConfigLoading config)
		{
			base.OnEndReadConfiguration(config);

			var listenerAddress = Config.GetAttribute("address", "0.0.0.0");
			var listenerPort = Config.GetAttribute("port", serverCertificate == null ? 3305 : 3306);

			Log.Info("Register Listener (port={0}, addr={1}, ssl={2})", listenerPort, listenerAddress, serverCertificate == null ? "<plain>" : serverCertificate.Subject);
			var endPoint = new IPEndPoint(IPAddress.Parse(listenerAddress), listenerPort);

			// start the listener
			serverTcp.RegisterListener(endPoint,
				serverCertificate != null ?
					new Action<Stream>(CreateSslHandler) :
					new Action<Stream>(CreateHandler)
			);
		} // proc OnEndReadConfiguration

		#endregion

		#region -- Create Handler ---------------------------------------------------------

		private void CreateHandler(Stream socket)
		{
			var protocol = this.GetService<OdetteFileTransferProtocolItem>(true);

			// start the protocol
			Task.Run(() => protocol.StartProtocolAsync(new OdetteNetworkStream(socket, "tcp:" + serverTcp.GetStreamInfo(socket), Config), false));
		} // proc CreateHandler

		private void CreateSslHandler(Stream socket)
			=> CreateSslHandler(socket, serverCertificate);

		private async void CreateSslHandler(Stream socket, X509Certificate2 certificate)
		{
			var ssl = new SslStream(socket, false, null, null);
			await ssl.AuthenticateAsServerAsync(serverCertificate, true, SslProtocols.Tls, false); // no revocation

			var protocol = this.GetService<OdetteFileTransferProtocolItem>(true);
			await protocol.StartProtocolAsync(new OdetteNetworkStream(ssl, "ssl:" + serverTcp.GetStreamInfo(socket), Config), false);
		} // func CreateSslHandler

		#endregion
	} // class OdetteListenerTcpItem
}
