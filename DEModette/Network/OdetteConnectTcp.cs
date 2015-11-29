using System;
using System.Net;
using System.Net.Security;
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

		public OdetteConnectTcpItem(IServiceProvider sp, string name)
			: base(sp, name)
		{
		} // ctor

		protected override void OnEndReadConfiguration(IDEConfigLoading config)
		{
			base.OnEndReadConfiguration(config);

			this.useSsl = Config.GetAttribute("ssl", useSsl);
			this.targetHost = Config.GetAttribute("addr", String.Empty);
			this.targetPort = Config.GetAttribute("port", useSsl ? targetPort : 3306);
			this.endPoint = null;
			this.channelName = null;
		} // proc OnEndReadConfiguration

		protected override void OnRunJob(CancellationToken cancellation)
		{
			if (endPoint == null)
				return;

		} // proc OnRunJob

		private async Task OnRunJobAsync()
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
							var ssl = new SslStream(stream, false);
							await ssl.AuthenticateAsClientAsync(targetHost);
							stream = ssl;
						}

						await protocol.StartProtocolAsync(new OdetteNetworkStream(stream, channelName, Config), true);
					}
					catch (Exception e)
					{
						Log.Except("Connection failed.", e);
					}
			}
		} // proc OnRunJobAsync

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
