#region -- copyright --
//
// Licensed under the EUPL, Version 1.1 or - as soon they will be approved by the
// European Commission - subsequent versions of the EUPL(the "Licence"); You may
// not use this work except in compliance with the Licence.
//
// You may obtain a copy of the Licence at:
// http://ec.europa.eu/idabc/eupl
//
// Unless required by applicable law or agreed to in writing, software distributed
// under the Licence is distributed on an "AS IS" basis, WITHOUT WARRANTIES OR
// CONDITIONS OF ANY KIND, either express or implied. See the Licence for the
// specific language governing permissions and limitations under the Licence.
//
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using TecWare.DE.Odette;

namespace TecWare.DE.Odette
{
	public sealed class Data
	{
		#region -- class OdetteFileService --------------------------------------------

		public sealed class OdetteFileService : IOdetteFileService
		{
			public void Dispose()
			{
			} // proc Dispose

			public string DestinationId => throw new NotImplementedException();

			string IOdetteFileService.DestinationId => throw new NotImplementedException();

			bool IOdetteFileService.SupportsInFiles => throw new NotImplementedException();

			bool IOdetteFileService.SupportsOutFiles => throw new NotImplementedException();

			Task<IOdetteFileWriter> IOdetteFileService.CreateInFileAsync(IOdetteFile file, string userData) => throw new NotImplementedException();
			
			IEnumerable<IOdetteFileEndToEnd> IOdetteFileService.GetEndToEnd() => throw new NotImplementedException();
			IEnumerable<Func<IOdetteFileReader>> IOdetteFileService.GetOutFiles() => throw new NotImplementedException();
			Task IOdetteFileService.UpdateOutFileStateAsync(IOdetteFileEndToEndDescription description) => throw new NotImplementedException();
		}

		#endregion

		#region -- class OdetteFtp ----------------------------------------------------

		private sealed class OdetteFtp : OdetteFtpCore
		{
			public OdetteFtp(IOdetteFtpChannel channel) 
				: base(channel)
			{
			}

			protected override IEnumerable<X509Certificate2> FindDestinationCertificates(string destinationId, bool partnerCertificate) => throw new NotImplementedException();

			protected override IOdetteFileService CreateFileService(string initiatorCode, string password) 
				=> throw new NotImplementedException();
			protected override void LogExcept(string message = null, Exception e = null, bool asWarning = false) => throw new NotImplementedException();
			protected override void LogInfo(string message) => throw new NotImplementedException();
			protected override string OdetteId => throw new NotImplementedException();
			protected override string OdettePassword => throw new NotImplementedException();
		}

		#endregion
	}
}
