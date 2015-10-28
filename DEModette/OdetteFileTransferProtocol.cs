using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TecWare.DE.Server;

namespace TecWare.DE.Odette
{
	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public class OdetteFileTransferProtocolItem : DEConfigLogItem
	{
		public OdetteFileTransferProtocolItem(IServiceProvider sp, string name)
			: base(sp, name)
		{
		} // ctor
	} // class OdetteFileTransferProtocolItem
}
