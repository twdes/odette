using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using TecWare.DE.Server;
using TecWare.DE.Stuff;

namespace TecWare.DE.Odette.Network
{
	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	internal sealed class OdetteConnectTcpItem : CronJobItem
	{
		private IPEndPoint endPoint = null;
		private bool useSsl = false;
		private int targetPort = 3305;
		private string targetHost = null;
		private string channelName = null;

		private bool inConnectionPhase = false;

		public OdetteConnectTcpItem(IServiceProvider sp, string name)
			: base(sp, name)
		{
		} // ctor

		protected override void OnEndReadConfiguration(IDEConfigLoading config)
		{
			base.OnEndReadConfiguration(config);

			this.useSsl = Config.GetAttribute("ssl", useSsl);
			this.targetHost = Config.GetAttribute("addr", String.Empty);
			this.targetPort = Config.GetAttribute("port", useSsl ? 3305 : 6619);
			this.endPoint = null;
			this.channelName = null;
		} // proc OnEndReadConfiguration

		protected override void OnRunJob(CancellationToken cancellation)
		{
			if (inConnectionPhase)
				Log.Info("Currently connecting...");
			else
				Task.Run(OnRunJobAsync);
		} // proc OnRunJob

		private async Task OnRunJobAsync()
		{
			inConnectionPhase = true;
			try
			{
				var timeoutSource = new CancellationTokenSource(30000);

				// resolve end point
				var serverTcp = this.GetService<IServerTcp>(true);
				if (endPoint == null)
					endPoint = await serverTcp.ResolveEndpointAsync(targetHost, targetPort, timeoutSource.Token);

				// register the connection
				if (endPoint != null)
				{
					var protocol = this.GetService<OdetteFileTransferProtocolItem>(true);

					// check if the protocol is running
					if (protocol.IsActiveProtocol(ChannelName))
						Log.Info("Protocol is already active.");
					else // create the connection
						try
						{
							var stream = await serverTcp.CreateConnectionAsync(endPoint, timeoutSource.Token);

							if (useSsl)
							{
								var ssl = new SslStream(stream, false, SslRemoteCertificateValidateCallback); // , SslLocalCertificateSelector
								await ssl.AuthenticateAsClientAsync(targetHost);

								var cert = ssl.RemoteCertificate;
								Log.Info($"Ssl active: auth={ssl.IsAuthenticated}, encrypt={ssl.IsEncrypted}, signed={ssl.IsSigned}\nissuer={cert.Issuer}\nsubject={cert.Subject}");
								stream = ssl;
							}

							await protocol.StartProtocolAsync(new OdetteNetworkStream(stream, channelName, Config), true);
						}
						catch (Exception e)
						{
							Log.Except("Connection failed.", e);
						}
				}
			}
			catch (Exception e)
			{
				Log.Except(e);
			}
			finally
			{
				inConnectionPhase = false;
			}
		} // proc OnRunJobAsync

		//private X509Certificate SslLocalCertificateSelector(object sender, string targetHost, X509CertificateCollection localCertificates, X509Certificate remoteCertificate, string[] acceptableIssuers)
		//{
		//	Log.Info("Host: {0}", targetHost);
		//	return null;
		//} // func SslLocalCertificateSelector

		private bool SslRemoteCertificateValidateCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
			=> NetworkHelper.SslRemoteCertificateValidate(Log, false, certificate, chain, sslPolicyErrors);

		protected override bool CanRunParallelTo(ICronJobItem o)
		{
			var other = o as OdetteConnectTcpItem;
			if (other == null || ChannelName == null)
				return true;

			return other.ChannelName == ChannelName;
		} // func CanRunParallelTo

		public string ChannelName
		{
			get
			{
				if (endPoint == null)
					return null;
				if (channelName == null)
					channelName = $"tcp:{endPoint.Address},{targetPort}";
				return channelName;
			}
		} // prop ChannelName

		public override bool IsSupportCancelation => false;
	} // class OdetteConnectTcpItem
}
