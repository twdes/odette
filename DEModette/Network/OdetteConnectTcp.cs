using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TecWare.DE.Server;
using TecWare.DE.Server.Stuff;
using TecWare.DE.Stuff;

namespace TecWare.DE.Odette.Network
{
	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	internal sealed class OdetteConnectTcpItem : DEConfigItem //, ICronJobItem
	{
		//private CronBound bound;

		public OdetteConnectTcpItem(IServiceProvider sp, string name)
			: base(sp, name)
		{
		} // ctor

		protected override void OnBeginReadConfiguration(IDEConfigLoading config)
		{
			base.OnBeginReadConfiguration(config);
		}

		protected override void OnEndReadConfiguration(IDEConfigLoading config)
		{
			base.OnEndReadConfiguration(config);
						
			//var protocol = this.GetService<OdetteFileTransferProtocolItem>(true);
			//var serverTcp = this.GetService<IServerTcp>(true);
			//Task.Run(() => protocol.StartProtocolAsync(new OdetteNetworkStream(serverTcp.Connect(new IPEndPoint(IPAddress.Parse("10.0.200.2"), 3305)), "test", ""), true));
    }

		//bool ICronJobExecute.CanRunParallelTo(ICronJobExecute other)
		//{
		//	return other != this;
		//} // func CanRunParallelTo

		//void ICronJobItem.NotifyNextRun(DateTime dt)
		//{
		//}

		//void ICronJobExecute.RunJob(CancellationToken cancellation)
		//{
		//}

		//CronBound ICronJobItem.Bound => bound;
		//string ICronJobItem.UniqueName => ConfigPath;
	} // class OdetteConnectTcpItem
}
