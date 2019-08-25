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
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Xml.Linq;
using TecWare.DE.Server;
using TecWare.DE.Stuff;

namespace TecWare.DE.Odette
{
	#region -- class OdetteFileTransferProtocolItem -----------------------------------

	/// <summary></summary>
	public class OdetteFileTransferProtocolItem : DEConfigLogItem
	{
		private static readonly XNamespace OdetteNamespace = "http://tecware-gmbh.de/dev/des/2014/odette";
		private static readonly XName xnCertificates = OdetteNamespace + "certificates";

		#region -- class OdetteFtp ----------------------------------------------------

		private sealed class OdetteFtp : OdetteFtpCore
		{
			private readonly OdetteFileTransferProtocolItem item;
			private readonly LoggerProxy log;

			#region -- Ctor/Dtor ------------------------------------------------------

			public OdetteFtp(OdetteFileTransferProtocolItem item, IOdetteFtpChannel channel) 
				: base(channel)
			{
				this.item = item ?? throw new ArgumentNullException(nameof(item));
				this.log = LoggerProxy.Create(item.Log, channel.Name);
			} // ctor

			#endregion

			#region -- Primitives - Log -----------------------------------------------

			protected override void LogInfo(string message)
				=> log.Info(message);

			protected override void LogExcept(string message = null, Exception e = null, bool asWarning = false)
			{
				if (e == null)
					log.LogMsg(asWarning ? LogMsgType.Warning : LogMsgType.Error, message);
				else
					log.LogMsg(asWarning ? LogMsgType.Warning : LogMsgType.Error, message ?? e.Message, e);
			} // proc LogExcept
			protected override bool IsDebugEnabled => item.IsDebugCommandsEnabled;

			#endregion

			protected override IOdetteFileService CreateFileService(string initiatorCode, string password)
				=> item.CreateFileService(initiatorCode, password);

			protected override IEnumerable<X509Certificate2> FindDestinationCertificates(string destinationId, bool partnerCertificate)
				=> item.FindCertificates(destinationId, partnerCertificate);

			protected override string OdetteId => item.OdetteId;
			protected override string OdettePassword => item.OdettePassword;
			
			/// <summary></summary>
			public LoggerProxy Log => log;
		} // class OdetteFtp

		#endregion

		private readonly DEThread threadProtocol;
		private bool debugCommands = false;

		private readonly DEList<OdetteFtp> activeProtocols;

		#region -- Ctor/Dtor ----------------------------------------------------------

		/// <summary></summary>
		/// <param name="sp"></param>
		/// <param name="name"></param>
		public OdetteFileTransferProtocolItem(IServiceProvider sp, string name)
			: base(sp, name)
		{
			threadProtocol = new DEThread(this, "Protocol", null); // start a thread to handle the protocol

			PublishItem(activeProtocols = new DEList<OdetteFtp>(this, "tw_protocols", "Protocols"));

			PublishItem(new DEConfigItemPublicAction("debugOn") { DisplayName = "Debug(on)" });
			PublishItem(new DEConfigItemPublicAction("debugOff") { DisplayName = "Debug(off)" });
		} // ctor

		/// <summary></summary>
		/// <param name="disposing"></param>
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				threadProtocol.Dispose();
				activeProtocols.Dispose();
			}

			base.Dispose(disposing);
		} // proc Dispose

		#endregion

		#region -- Protocol List ------------------------------------------------------

		private void AddProtocol(OdetteFtp oftp)
			=> activeProtocols.Add(oftp);
		
		private void RemoveProtocol(OdetteFtp oftp)
			=> activeProtocols.Remove(oftp);
		
		/// <summary></summary>
		/// <param name="channelName"></param>
		/// <returns></returns>
		public bool IsActiveProtocol(string channelName)
			=> activeProtocols.FindIndex(o => o.Channel.Name == channelName) >= 0;
		
		#endregion

		#region -- FindCertificates ---------------------------------------------------

		/// <summary>Finds the certificates for the destination.</summary>
		/// <param name="destinationId"></param>
		/// <param name="partnerCertificate"><c>true</c> the public key of the partner, <c>false</c> the private key used for this destination.</param>
		/// <returns></returns>
		public IEnumerable<X509Certificate2> FindCertificates(string destinationId, bool partnerCertificate)
		{
			var collected = new List<X509Certificate2>();
			foreach (var x in Config.Elements(xnCertificates))
			{
				if (String.Compare(x.GetAttribute("destinationId", String.Empty), destinationId, StringComparison.OrdinalIgnoreCase) == 0)
				{
					try
					{
						foreach (var cert in ProcsDE.FindCertificate(x.GetAttribute(partnerCertificate ? "partner" : "my", String.Empty)))
							if (cert != null)
								collected.Add(cert);
					}
					catch (Exception e)
					{
						Log.Warn(e);
					}
				}
			}
			return collected;
		} // func FindCertificateFromDestination

		#endregion

		#region -- StartProtocolAsync, CreateFileService ------------------------------

		/// <summary></summary>
		/// <param name="channel"></param>
		/// <param name="initiator"></param>
		public void StartProtocol(IOdetteFtpChannel channel, bool initiator)
		{
			var protocol = new OdetteFtp(this, channel);
			AddProtocol(protocol);

			threadProtocol.RootContext.Post(s =>
				{
					protocol
						.RunAsync(initiator)
						.ContinueWith(
							t =>
							{
								try
								{
									t.Wait();
									protocol.DisconnectAsync().AwaitTask();
								}
								catch(Exception e)
								{
									protocol.Log.Except("Abnormal termination.", e);
								}
								finally
								{
									RemoveProtocol(protocol);
								}
							},
							TaskContinuationOptions.ExecuteSynchronously
						);

				}, 
				null);
		}

		internal OdetteFileService CreateFileService(string destinationId, string password)
			=> new OdetteFileService(this, destinationId, password);

		#endregion

		[DEConfigHttpAction("debugOn", IsSafeCall = true, SecurityToken = SecuritySys)]
		private XElement SetDebugCommandsOn()
			=> SetDebugCommands(true);

		[DEConfigHttpAction("debugOff", IsSafeCall = true, SecurityToken = SecuritySys)]
		private XElement SetDebugCommandsOff()
			=> SetDebugCommands(false);

		[DEConfigHttpAction("debug", IsSafeCall = true, SecurityToken = SecuritySys)]
		private XElement SetDebugCommands(bool on = false)
		{
			debugCommands = on;
			OnPropertyChanged(nameof(IsDebugCommandsEnabled));
			return new XElement("return", new XAttribute("debug", debugCommands));
		} // func SetDebugCommands

		/// <summary></summary>
		public string OdetteId => Config.GetAttribute("odetteId", String.Empty);
		/// <summary></summary>
		public string OdettePassword => Config.GetAttribute("odettePassword", String.Empty);


		/// <summary></summary>
		[
		PropertyName("tw_oftp_debug"),
		DisplayName("Debug Commands"),
		Description("Should the system log the in and outgoing oftp packets."),
		Category("OFTP"),
		Format("{0:XiB}")
		]
		public bool IsDebugCommandsEnabled => debugCommands;
	} // class OdetteFileTransferProtocolItem

	#endregion
}
