using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TecWare.DE.Server;

namespace TecWare.DE.Odette.Services
{
	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public class DirectoryFileServiceItem : DEConfigLogItem
	{
		public DirectoryFileServiceItem(IServiceProvider sp, string name)
			: base(sp, name)
		{
		} // ctor
	} // class DirectoryFileServiceItem
}
