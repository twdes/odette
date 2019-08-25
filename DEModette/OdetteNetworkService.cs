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

		public static bool SslRemoteCertificateValidate(LoggerProxy log, bool skipInvalidCertificate, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
		{
			if (sslPolicyErrors == SslPolicyErrors.None)
				return true;

			using (var m = log.CreateScope(skipInvalidCertificate ? LogMsgType.Warning : LogMsgType.Error))
			{
				m.WriteLine("Remote certification validation failed ({0}).", sslPolicyErrors);
				m.WriteLine();
				m.WriteLine("Remote Certificate:");
				if (certificate != null)
				{
					m.WriteLine("  Subject: {0}", certificate.Subject);
					m.WriteLine("  CertHash: {0}", certificate.GetCertHashString());
					m.WriteLine("  Expiration: {0}", certificate.GetExpirationDateString());
					m.WriteLine("  Serial: {0}", certificate.GetSerialNumberString());
					m.WriteLine("  Algorithm: {0}", certificate.GetKeyAlgorithmParametersString());
				}
				else
				{
					m.WriteLine("  <null>");
					// no chance to skip
					return skipInvalidCertificate;
				}

				m.WriteLine("Chain:");

				var i = 0;
				foreach (var c in chain.ChainElements)
				{
					m.Write("  - {0}", c.Certificate?.Subject);
					if (i < chain.ChainStatus.Length)
						m.WriteLine(" --> {0}, {1}", chain.ChainStatus[i].Status, chain.ChainStatus[i].StatusInformation);
					else
						m.WriteLine();
					i++;
				}
			}

			return skipInvalidCertificate;
		} // func SslRemoteCertificateValidate
	} // class NetworkHelper

	#endregion
}
