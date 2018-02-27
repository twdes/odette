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
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using TecWare.DE.Stuff;

namespace TecWare.DE.Odette
{
	#region -- class OdetteNetworkException -------------------------------------------

	/// <summary></summary>
	public class OdetteNetworkException : Exception
	{
		/// <summary></summary>
		/// <param name="message"></param>
		public OdetteNetworkException(string message)
			: base(message)
		{
		} // ctor
	} // class OdetteNetworkException

	#endregion

	#region -- interface IOdetteFtpChannel --------------------------------------------

	/// <summary></summary>
	public interface IOdetteFtpChannel : IDisposable
	{
		/// <summary>Sends a disconnect to communication partner.</summary>
		/// <returns></returns>
		Task DisconnectAsync();

		/// <summary>Receive a command.</summary>
		/// <param name="buffer">Buffer that receives a command, it must have the maximum buffer size.</param>
		/// <returns>Returns the size of the received command or zero, if the channel is disconnected.</returns>
		Task<int> ReceiveAsync(byte[] buffer);
		/// <summary>Send a command.</summary>
		/// <param name="buffer">Buffer that contains a command.</param>
		/// <param name="filled">Length of the command.</param>
		/// <returns></returns>
		Task SendAsync(byte[] buffer, int filled);

		/// <summary>Unique name for channel, e.g. remote ip + protocol.</summary>
		string Name { get; }
		/// <summary>UserData, that will sent to the communication partner on connect.</summary>
		string UserData { get; }
		/// <summary>Returns the initial capabilities.</summary>
		OdetteCapabilities InitialCapabilities { get; }
	} // class IOdetteFtpChannel

	#endregion

	#region -- class NetworkHelper ----------------------------------------------------

	internal static class NetworkHelper
	{
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
