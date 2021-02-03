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
using System.IO;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using TecWare.DE.Stuff;

namespace TecWare.DE.Odette
{
	#region -- class NetworkHelper ----------------------------------------------------

	internal static class NetworkHelper
	{
		public static IOdetteFtpChannel CreateNetworkChannel(Stream stream, string name, XElement xConfig)
		{
			var capabilities = OdetteCapabilities.None;

			if (xConfig.GetAttribute("allowBufferCompression", false))
				capabilities |= OdetteCapabilities.BufferCompression;
			if (xConfig.GetAttribute("allowRestart", false))
				capabilities |= OdetteCapabilities.Restart;
			if (xConfig.GetAttribute("allowSecureAuthentification", false))
				capabilities |= OdetteCapabilities.SecureAuthentification;

			return new OdetteNetworkStream(stream, name, xConfig.GetAttribute("userData", String.Empty), capabilities);
		} // func CreateNetworkChannel
	} // class NetworkHelper

	#endregion
}
