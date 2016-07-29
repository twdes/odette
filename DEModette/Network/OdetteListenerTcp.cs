using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using TecWare.DE.Server;
using TecWare.DE.Stuff;

namespace TecWare.DE.Odette.Network
{
	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	internal sealed class OdetteListenerTcpItem : DEConfigItem
	{
		private const SslProtocols defaultSslProtocols = SslProtocols.Default | SslProtocols.Tls11 | SslProtocols.Tls12;

		private IServerTcp serverTcp;

		private IListenerTcp currentListener = null;
		private X509Certificate2 serverCertificate = null;
		private bool skipInvalidCertificate = false;
		private bool clientCertificateRequired = true;
		private SslProtocols sslProtocols = defaultSslProtocols;

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

			var useSsl = config.ConfigNew.GetAttribute("ssl", String.Empty);
			if (String.IsNullOrEmpty(useSsl))
				serverCertificate = null;
			else
			{
				Log.Info("Try to locate certificate: {0}", useSsl);
				serverCertificate = ProcsDE.FindCertificate(useSsl).FirstOrDefault(); // todo: select server certificate
				if (serverCertificate == null)
					throw new ArgumentException("Server certificate not found.");
			}
		} // proc OnBeginReadConfiguration

		protected override void OnEndReadConfiguration(IDEConfigLoading config)
		{
			base.OnEndReadConfiguration(config);

			var listenerAddress = Config.GetAttribute("address", "0.0.0.0");
			var listenerPort = Config.GetAttribute("port", serverCertificate == null ? 3305 : 6619);
			skipInvalidCertificate = Config.GetAttribute("skipInvalidCertificate", false);
			clientCertificateRequired = Config.GetAttribute("clientCertificateRequired", true);
			sslProtocols = (SslProtocols)Config.GetAttribute("sslProtocols", (int)defaultSslProtocols);

			Log.Info("Register Listener (port={0}, addr={1}, ssl={2})", listenerPort, listenerAddress, serverCertificate == null ? "<plain>" : serverCertificate.Subject);
			var endPoint = new IPEndPoint(IPAddress.Parse(listenerAddress), listenerPort);

			// start the listener
			Procs.FreeAndNil(ref currentListener);
			currentListener = serverTcp.RegisterListener(endPoint,
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
			SslStream ssl = null;
			try
			{
				ssl = new SslStream(socket, false, SslRemoteCertificateValidateCallback, null);
				
				await ssl.AuthenticateAsServerAsync(serverCertificate, clientCertificateRequired, sslProtocols, false); // no revocation

				var protocol = this.GetService<OdetteFileTransferProtocolItem>(true);
				await protocol.StartProtocolAsync(new OdetteNetworkStream(ssl, "ssl:" + serverTcp.GetStreamInfo(socket), Config), false);
			}
			catch (Exception e)
			{
				Log.Except("Protocol initialization failed.", e);
				ssl?.Dispose();
			}
		} // func CreateSslHandler

		//private X509Certificate SslLocalCertificateSelector(object sender, string targetHost, X509CertificateCollection localCertificates, X509Certificate remoteCertificate, string[] acceptableIssuers)
		//{
		//	return null;
		//} // func SslLocalCertificateSelector

		private bool SslRemoteCertificateValidateCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
			=> NetworkHelper.SslRemoteCertificateValidate(Log, skipInvalidCertificate, certificate, chain, sslPolicyErrors);

		#endregion
	} // class OdetteListenerTcpItem
}
