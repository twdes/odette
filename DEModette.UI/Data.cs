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
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using TecWare.DE.Data;
using TecWare.DE.Stuff;

namespace TecWare.DE.Odette
{
	#region -- interface IOdetteDestination ----------------------------------------------

	public interface IOdetteDestination : INotifyPropertyChanged
	{
		Task AddOutFileAsync(string fileName, string userData);
		void Remove();

		Task RunAsync();

		string Name { get; }
		string DestinationId { get; set; }
		string Password { get; set; }

		string DestinationHost { get; set; }
		int DestinationPort { get; set; }
		bool UseSsl { get; set; }

		bool IsRunning { get; }
	} // interface IOdetteDestination

	#endregion

	public sealed class DataLogLine
	{
		private readonly string text;

		public DataLogLine(string text)
		{
			this.text = text ?? throw new ArgumentNullException(nameof(text));
		}

		public string Text => text;
	} // class DataLogLine

	public sealed class Data : ObservableObject, IEnumerable<IOdetteDestination>, INotifyCollectionChanged
	{
		#region -- class OdetteFileWriter ---------------------------------------------

		private sealed class OdetteFileWriter : IOdetteFileWriter, IOdetteFilePosition, IDisposable
		{
			private readonly OdetteInFile file;
			private readonly Stream stream;

			public OdetteFileWriter(OdetteInFile file, Stream stream)
			{
				this.file = file ?? throw new ArgumentNullException(nameof(file));
				this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
			} // ctor

			public void Dispose()
			{
				stream.Dispose();
			} // proc Dispose

			public Task WriteAsync(byte[] buf, int offset, int count, bool isEoR)
				=> stream.WriteAsync(buf, offset, count);

			public async Task<long> SeekAsync(long position)
			{
				var newpos = position << 10;
				if (newpos > stream.Length)
					newpos = stream.Length & ~0x3FF;

				if (newpos < stream.Length)
					stream.Position = newpos;
				else
					await Task.Run(() => stream.SetLength(newpos)); // truncate current data

				return newpos;
			} // func SeekAsync

			public Task CommitFileAsync(long recordCount, long unitCount)
				=> file.CommitFileAsync(recordCount, unitCount);

			public IOdetteFile Name => file;

			public long TotalLength => stream.Length;
			public long RecordCount => 0;
		} // class OdetteFileWriter

		#endregion

		#region -- class OdetteFileReader ---------------------------------------------

		private sealed class OdetteFileReader : IOdetteFileReader, IOdetteFilePosition, IDisposable
		{
			private readonly OdetteOutFile file;
			private readonly Stream stream;

			public OdetteFileReader(OdetteOutFile file, Stream stream)
			{
				this.file = file ?? throw new ArgumentNullException(nameof(file));
				this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
			} // ctor

			public void Dispose()
				=> stream?.Dispose();

			public async Task<(int readed, bool isEoR)> ReadAsync(byte[] buf, int offset, int count)
			{
				var readed = await stream.ReadAsync(buf, offset, count);
				if (readed == 0)
					readed = -1; // enforced eof

				return (readed, readed < count);
			} // // func ReadAsync

			public Task<long> SeekAsync(long position)
			{
				var newpos = position << 10;
				if (newpos > stream.Length)
					newpos = stream.Length & ~0x3FF;

				if (newpos < stream.Length)
					stream.Position = newpos;
				else
					throw new ArgumentOutOfRangeException(nameof(position));

				return Task.FromResult(newpos);
			} // func SeekAsync

			public Task SetTransmissionErrorAsync(OdetteAnswerReason answerReason, string reasonText, bool retryFlag)
				=> file.SetTransmissionErrorAsync(answerReason, reasonText, retryFlag);

			public Task SetTransmissionStateAsync()
				=> file.SetTransmissionStateAsync();

			public IOdetteFile Name => file;
			public string UserData => file.UserData;

			public long TotalLength => stream.Length;
			public long RecordCount => 0;
		} // class OdetteFileReader

		#endregion

		#region -- class OdetteFile ---------------------------------------------------

		private abstract class OdetteFile : ObservableObject, IOdetteFile, IEquatable<IOdetteFile>
		{
			private readonly OdetteFileService fileService;

			private readonly FileInfo fileStateInfo;
			private readonly string virtualFileName;
			private readonly DateTime fileStamp;
			private readonly string userData;

			protected OdetteFile(OdetteFileService fileService, FileInfo fileStateInfo)
			{
				this.fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
				this.fileStateInfo = fileStateInfo ?? throw new ArgumentNullException(nameof(fileStateInfo));

				var x = XDocument.Load(fileStateInfo.FullName);

				virtualFileName = x.Root.GetAttribute("vname", "vn");
				fileStamp = fileStateInfo.CreationTime;
				userData = x.Root.GetAttribute("userData", null);

				ParseState(x.Root);
			} // ctor

			protected OdetteFile(OdetteFileService fileService, string virtualFileName, DateTime fileStamp, string userData, string ext)
			{
				this.fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
				fileStateInfo = new FileInfo(Path.Combine(fileService.BasePath, fileStamp.ToString("yyyyMMdd_HHmmss_ffff.") + ext));

				this.virtualFileName = virtualFileName ?? throw new ArgumentNullException(nameof(virtualFileName));
				this.fileStamp = fileStamp;
				this.userData = userData ?? String.Empty;
			} // ctor

			public bool Equals(IOdetteFile other)
			{
				return String.Compare(virtualFileName, other.VirtualFileName, StringComparison.OrdinalIgnoreCase) == 0
					|| fileStamp == other.FileStamp
					|| Originator == other.Originator;
			} // func Equals

			protected virtual void CreateState(XElement x)
			{
			} // proc CreateState

			protected virtual void ParseState(XElement x)
			{
			} // proc ParseState

			protected override void OnPropertyChanged(string propertyName)
				=> fileService.Data.SynchronizeUI(new Action(() => base.OnPropertyChanged(propertyName)), true);

			public Task SaveStateAsync()
			{
				var x = new XDocument(new XElement("state"));
				CreateState(x.Root);

				x.Root.SetAttributeValue("vname", virtualFileName);
				x.Root.SetAttributeValue("userData", userData);

				return Task.Run(() =>
				{
					if (!fileStateInfo.Directory.Exists)
						fileStateInfo.Directory.Create();

					x.Save(fileStateInfo.FullName);
					fileStateInfo.Refresh();
					fileStateInfo.CreationTime = fileStamp;
				});
			} // proc SaveStateAsync

			public string VirtualFileName => virtualFileName;
			public DateTime FileStamp => fileStamp;
			public abstract string Originator { get; }

			public string UserData => userData;

			public abstract string Type { get; }
			public abstract string Status { get; }

			public OdetteFileService FileService => fileService;
			protected FileInfo StateInfo => fileStateInfo;
		} // class OdetteFile

		#endregion

		#region -- class OdetteInFile -------------------------------------------------

		private sealed class OdetteInFile : OdetteFile, IOdetteFileEndToEnd
		{
			private string sourceId;
			private OdetteInFileState state;

			public OdetteInFile(OdetteFileService fileService, FileInfo fileStateInfo)
				: base(fileService, fileStateInfo)
			{
			} // ctor

			public OdetteInFile(OdetteFileService fileService, IOdetteFile file, string userData, string sourceId)
				: base(fileService, file.VirtualFileName, file.FileStamp, userData, "in")
			{
				this.sourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
				this.state = OdetteInFileState.Pending;
			} // ctor

			protected override void CreateState(XElement x)
			{
				x.SetAttributeValue("source", sourceId);
				x.SetAttributeValue("state", state.ToString());

				base.CreateState(x);
			} // proc CreateState

			protected override void ParseState(XElement x)
			{
				base.ParseState(x);

				sourceId = x.GetAttribute("source", null);
				state = x.GetAttribute("state", OdetteInFileState.Pending);
			} // proc ParseState

			internal async Task<IOdetteFileWriter> CreateWriterAsync()
			{
				await SaveStateAsync();

				// change extension
				var dataFileInfo = new FileInfo(Path.ChangeExtension(StateInfo.FullName, ".data"));
				var dst = await Task.Run(() => dataFileInfo.Create());

				// return writer
				return new OdetteFileWriter(this, dst);
			} // func CreateWriterAsync

			#region -- IOdetteFileEndToEnd members ---------------------------------------------

			async Task IOdetteFileEndToEnd.CommitAsync()
			{
				// mark file as committed by end point
				state = OdetteInFileState.Finished;
				await SaveStateAsync();
				OnPropertyChanged(nameof(Status));
			} // proc IOdetteFileEndToEnd.CommitAsync

			IOdetteFile IOdetteFileEndToEndDescription.Name => this;
			int IOdetteFileEndToEndDescription.ReasonCode => 0;
			string IOdetteFileEndToEndDescription.ReasonText => String.Empty;

			#endregion

			internal async Task CommitFileAsync(long recordCount, long unitCount)
			{
				state = OdetteInFileState.PendingEndToEnd; // skip receive for now
				await SaveStateAsync();
				OnPropertyChanged(nameof(Status));
			} // proc CommitFileAsync

			public override string Originator => sourceId;

			public OdetteInFileState InState => state;
			public override string Status => state.ToString();
			public override string Type => "IN";
		} // class OdetteInFileItem

		#endregion

		#region -- class OdetteOutFile ------------------------------------------------

		private sealed class OdetteOutFile : OdetteFile
		{
			private FileInfo sourceFile;

			private OdetteOutFileState state;
			private int answerReason = 0;
			private string reasonText = null;

			public OdetteOutFile(OdetteFileService fileService, FileInfo fileStateInfo)
				: base(fileService, fileStateInfo)
			{
			} // ctor

			public OdetteOutFile(OdetteFileService fileService, FileInfo sourceFile, string userData)
				: base(fileService, sourceFile.Name, sourceFile.CreationTime, userData, "out")
			{
				this.sourceFile = sourceFile ?? throw new ArgumentNullException(nameof(sourceFile));
				state = OdetteOutFileState.Sent;
			} // ctor

			protected override void CreateState(XElement x)
			{
				x.SetAttributeValue("state", state.ToString());
				x.SetAttributeValue("file", sourceFile.FullName);

				x.SetAttributeValue("reason", answerReason);
				x.SetAttributeValue("reasonText", reasonText);

				base.CreateState(x);
			} // proc CreateState

			protected override void ParseState(XElement x)
			{
				base.ParseState(x);

				sourceFile = new FileInfo(x.GetAttribute("file", null));
				state = x.GetAttribute("state", OdetteOutFileState.New);

				answerReason = x.GetAttribute("reason", 0);
				reasonText = x.GetAttribute("reasonText", null);
			} // proc ParseState

			internal IOdetteFileReader CreateReader()
				=> new OdetteFileReader(this, sourceFile.OpenRead());

			internal async Task SetTransmissionErrorAsync(OdetteAnswerReason answerReason, string reasonText, bool retryFlag)
			{
				this.answerReason = (int)answerReason;
				this.reasonText = reasonText;

				state = OdetteOutFileState.Finished; // or OdetteOutFileState.Sent with retry;
				await SaveStateAsync();
				OnPropertyChanged(nameof(Status));
			} // func SetTransmissionErrorAsync

			internal async Task SetTransmissionStateAsync()
			{
				state = OdetteOutFileState.WaitEndToEnd;
				await SaveStateAsync();
				OnPropertyChanged(nameof(Status));
			} // func SetTransmissionStateAsync

			public async Task SetEndToEndAsync(IOdetteFileEndToEndDescription endToEndDescription)
			{
				answerReason = endToEndDescription.ReasonCode;
				reasonText = endToEndDescription.ReasonText;
				
				state = OdetteOutFileState.Finished; // OdetteOutFileState.ReceivedEndToEnd;
				OnPropertyChanged(nameof(Status));
				await SaveStateAsync();
			} // func SetEndToEndAsync

			public override string Originator => FileService.DestinationId;

			public OdetteOutFileState OutState => state;
			public override string Status => state.ToString();
			public override string Type => "OUT";
		} // class OdetteOutFile

		#endregion

		#region -- class OdetteFileService --------------------------------------------

		private sealed class OdetteFileService : ObservableObject, IOdetteFileService, IOdetteDestination, IEnumerable<IOdetteFile>, INotifyCollectionChanged
		{
			public event NotifyCollectionChangedEventHandler CollectionChanged;

			private readonly Data data;
			private readonly List<OdetteFile> files = new List<OdetteFile>();
			private readonly DirectoryInfo baseDirectory;

			private readonly string name = null;
			private string destinationId = null;
			private string destinationPassword = null;

			private string destinationHost = null;
			private int destinationPort = 5555;
			private bool useSsl = false;

			private volatile bool isRunning = false;

			public OdetteFileService(Data data, string name, XElement xConfig)
			{
				this.data = data ?? throw new ArgumentNullException(nameof(data));
				this.name = name ?? throw new ArgumentNullException(nameof(name));

				baseDirectory = new DirectoryInfo(Path.Combine(data.BasePath, name));

				if (xConfig != null)
				{
					destinationId = xConfig.GetAttribute("id", null);
					destinationPassword = xConfig.GetAttribute("password", null);

					destinationHost = xConfig.GetAttribute("host", null);
					destinationPort = xConfig.GetAttribute("port", 5555);
					useSsl = xConfig.GetAttribute("useSsl", false);
				}

				// collect file state
				if (baseDirectory.Exists)
				{
					foreach (var fi in baseDirectory.EnumerateFiles())
					{
						if (String.Compare(fi.Extension, ".in", StringComparison.OrdinalIgnoreCase) == 0)
							AddFileCore(new OdetteInFile(this, fi));
						else if (String.Compare(fi.Extension, ".out", StringComparison.OrdinalIgnoreCase) == 0)
							AddFileCore(new OdetteOutFile(this, fi));
					}
				}
			} // ctor

			public void Dispose()
			{
			} // proc Dispose

			public XElement GetXml()
			{
				var x = new XElement("fs");
				x.SetAttributeValue("name", name);
				x.SetAttributeValue("id", destinationId);
				x.SetAttributeValue("password", destinationPassword);
				x.SetAttributeValue("host", destinationHost);
				x.SetAttributeValue("port", destinationPort);
				x.SetAttributeValue("useSsl", useSsl);
				return x;
			} // func GetXml

			protected override void OnPropertyChanged(string propertyName)
			{
				switch(propertyName)
				{
					case nameof(DestinationId):
					case nameof(Password):
					case nameof(DestinationHost):
					case nameof(DestinationPort):
					case nameof(UseSsl):
						data.SetDirty();
						break;
				}

				data.SynchronizeUI(() => base.OnPropertyChanged(propertyName), true);
			} // proc OnPropertyChanged

			private void OnCollectionReset()
			{
				void OnCollectionResetUI()
					=> CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));

				data.SynchronizeUI(OnCollectionResetUI);
			} // proc OnCollectionReset

			public void Remove()
				=> data.RemoveDestination(this);

			private void AddFileCore(OdetteFile file)
			{
				lock (files)
					files.Add(file);
				OnCollectionReset();
			} // proc AddFileCore

			public Task AddOutFileAsync(string fileName, string userData)
			{
				var outFile = new OdetteOutFile(this, new FileInfo(fileName), userData);
				AddFileCore(outFile);
				return outFile.SaveStateAsync();
			} // proc AddOutFileAsync

			Task<IOdetteFileWriter> IOdetteFileService.CreateInFileAsync(IOdetteFile file, string userData)
			{
				if (file is IOdetteFileDescription fileDescription)
				{
					switch (fileDescription.Format)
					{
						case OdetteFileFormat.Unstructured:
						case OdetteFileFormat.Text:
							break;
						default:
							throw new ArgumentException("Unsupported format.");
					}
				}

				// mark file as receiving
				var newFile = new OdetteInFile(this, file, userData, file.Originator);
				AddFileCore(newFile);
				return newFile.CreateWriterAsync();
			} // func IOdetteFileService.CreateInFileAsync

			IEnumerable<IOdetteFileEndToEnd> IOdetteFileService.GetEndToEnd()
			{
				lock (files)
					return files.OfType<OdetteInFile>().Where(c => c.InState == OdetteInFileState.PendingEndToEnd).ToArray();
			} // func IOdetteFileService.GetEndToEnd

			IEnumerable<Func<IOdetteFileReader>> IOdetteFileService.GetOutFiles()
			{
				lock (files)
				{
					return files.OfType<OdetteOutFile>().Where(c => c.OutState == OdetteOutFileState.Sent).ToArray()
						.Select(c => new Func<IOdetteFileReader>(c.CreateReader));
				}
			} // func IOdetteFileService.GetOutFiles

			async Task<bool> IOdetteFileService.UpdateOutFileStateAsync(IOdetteFileEndToEndDescription description)
			{
				OdetteOutFile outFile;
				lock (files)
					outFile = files.OfType<OdetteOutFile>().FirstOrDefault(c => c.OutState == OdetteOutFileState.WaitEndToEnd && c.Equals(description.Name));
				if (outFile != null)
				{
					await outFile.SetEndToEndAsync(description);
					return true;
				}
				return false;
			} // func IOdetteFileService.UpdateOutFileStateAsync

			public IEnumerator<IOdetteFile> GetEnumerator()
			{
				lock (files)
				{
					foreach (var cur in files)
						yield return cur;
				}
			} // proc GetEnumerator

			IEnumerator IEnumerable.GetEnumerator()
				=> GetEnumerator();

			public async Task RunAsync()
			{
				if (isRunning)
					return;

				IsRunning = true;
				try
				{
					using (var cl = new TcpClient())
					{
						await cl.ConnectAsync(destinationHost, destinationPort);
						
						var stream = (Stream)cl.GetStream();
						if (useSsl)
						{
							var ssl = new SslStream(stream, false);
							await ssl.AuthenticateAsClientAsync(destinationHost);
							stream = ssl;
						}

						using (var channel = new OdetteNetworkStream(stream, name, null, OdetteCapabilities.Send | OdetteCapabilities.Receive))
						{
							var ftp = new OdetteFtp(data, channel);
							await ftp.RunAsync(true);
						}
					}
				}
				finally
				{
					IsRunning = false;
				}
			} // proc RunAsync

			bool IOdetteFileService.SupportsInFiles => true;
			bool IOdetteFileService.SupportsOutFiles => true;

			public string Name => name;
			/// <summary>Other endpoint of the communication.</summary>
			public string DestinationId { get => destinationId; set => Set(ref destinationId, value, nameof(DestinationId)); }

			public string Password { get => destinationPassword; set => Set(ref destinationPassword, value, nameof(Password)); }

			public string DestinationHost { get => destinationHost; set => Set(ref destinationHost, value, nameof(DestinationHost)); }
			public int DestinationPort { get => destinationPort; set => Set(ref destinationPort, value, nameof(DestinationPort)); }
			public bool UseSsl { get => useSsl; set => Set(ref useSsl, value, nameof(UseSsl)); }

			public bool IsRunning
			{
				get => isRunning;
				private set
				{
					if (isRunning != value)
					{
						isRunning = value;
						OnPropertyChanged(nameof(IsRunning));
					}
				}
			} // prop IsRunning

			public string BasePath => baseDirectory.FullName;

			public Data Data => data;
		} // class OdetteFileService

		#endregion

		#region -- class OdetteFtp ----------------------------------------------------

		private sealed class OdetteFtp : OdetteFtpCore
		{
			private readonly Data data;

			public OdetteFtp(Data data, IOdetteFtpChannel channel)
				: base(channel)
			{
				this.data = data ?? throw new ArgumentNullException(nameof(data));
			} // ctor

			protected override IEnumerable<X509Certificate2> FindDestinationCertificates(string destinationId, bool partnerCertificate)
				=> throw new NotImplementedException();

			protected override IOdetteFileService CreateFileService(string initiatorCode, string password)
				=> data.FindFileService(initiatorCode, password);

			protected override void LogExcept(string message = null, Exception e = null, bool asWarning = false)
			{
				if (data == null)
					return;
				if (e != null)
					data.AddLog(e.ToString());
				else
					data.AddLog(message);
			} // proc LogExcept

			protected override void LogInfo(string message)
				=> data.AddLog(message);

			/// <summary>Own id</summary>
			protected override string OdetteId => data.odetteId;
			/// <summary>Own password</summary>
			protected override string OdettePassword => data.odettePassword;
		} // class OdetteFtp

		#endregion

		public event NotifyCollectionChangedEventHandler CollectionChanged;

		private readonly SynchronizationContext synchronizationContext;
		private readonly string profileName;
		private readonly FileInfo profileFileInfo;
		private bool isDirty = false;

		private string odetteId;
		private string odettePassword;

		private bool listenLocal;
		private int listenPort;
		private volatile TcpListener listener = null;

		private readonly ObservableCollection<DataLogLine> log = new ObservableCollection<DataLogLine>();
		private readonly List<OdetteFileService> destinations = new List<OdetteFileService>();

		public Data(string profileName = null)
		{
			synchronizationContext = SynchronizationContext.Current ?? throw new ArgumentNullException(nameof(SynchronizationContext), "No synchronization context");
			this.profileName = profileName ?? "default";

			profileFileInfo = new FileInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TecWare", "Oftp", this.profileName + ".profile"));
			Read();
		} // ctor

		private void SetDirty()
			=> Set(ref isDirty, true, nameof(IsDirty));
		
		private void ResetDirty()
			=> Set(ref isDirty, false, nameof(IsDirty));

		private XDocument GetEmptyDocument()
		{
			return new XDocument(
				new XElement("oftp",
					new XElement("listen")
				)
			);
		} // proc GetEmptyDocument

		private void Read()
		{
			var xDoc = profileFileInfo.Exists ? XDocument.Load(profileFileInfo.FullName) : GetEmptyDocument();

			// root parameter
			odetteId = xDoc.Root.GetAttribute("id", null);
			odettePassword = xDoc.Root.GetAttribute("password", null);

			// listener parameter
			var xListen = xDoc.Root.Element("listen");
			listenPort = xListen.GetAttribute("port", 5555);
			listenLocal = xListen.GetAttribute("local", true);

			// file services
			lock (destinations)
			{
				foreach (var x in xDoc.Root.Elements("fs"))
				{
					var n = x.GetAttribute("name", null);
					if (!String.IsNullOrEmpty(n))
						destinations.Add(new OdetteFileService(this, n, x));
				}
			}

			OnPropertyChanged(nameof(OdetteId));
			OnPropertyChanged(nameof(OdettePassword));
			OnPropertyChanged(nameof(ListenPort));
			OnPropertyChanged(nameof(ListenLocal));

			ResetDirty();
			OnCollectionReset();
		} // proc Read

		public async Task SaveAsync()
		{
			var xDoc = GetEmptyDocument();

			// root parameter
			xDoc.Root.SetAttributeValue("id", odetteId);
			xDoc.Root.SetAttributeValue("password", odettePassword);

			// listener parameter
			var xListen = xDoc.Root.Element("listen");
			xListen.SetAttributeValue("port", listenPort);
			xListen.SetAttributeValue("local", listenLocal);

			// file services
			lock (destinations)
				xDoc.Root.Add(destinations.Select(c => c.GetXml()));

			await Task.Run(() =>
			{
				if (!profileFileInfo.Directory.Exists)
					profileFileInfo.Directory.Create();
				xDoc.Save(profileFileInfo.FullName);
			});

			ResetDirty();
		} // proc SaveAsync

		protected override void OnPropertyChanged(string propertyName)
		{
			switch(propertyName)
			{
				case nameof(OdetteId):
				case nameof(OdettePassword):
				case nameof(ListenPort):
				case nameof(ListenLocal):
					SetDirty();
					break;
			}
			base.OnPropertyChanged(propertyName);
		} // proc OnPropertyChanged

		private void OnCollectionReset()
			=> CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));

		private void SynchronizeUI(Action action, bool post = false)
		{
			var d = new SendOrPostCallback(s => ((Action)s).Invoke());
			if (post)
				synchronizationContext.Post(d, action);
			else
				synchronizationContext.Send(d, action);
		} // proc SynchronizeUI

		public IOdetteDestination AddDestination(string name)
		{
			var newDest = new OdetteFileService(this, name, null);
			lock (destinations)
				destinations.Add(newDest);
			SetDirty();
			OnCollectionReset();
			return newDest;
		} // proc AddDestination

		private void RemoveDestination(OdetteFileService destination)
		{
			lock (destinations)
				destinations.Remove(destination);

			SetDirty();
			OnCollectionReset();
		} // proc RemoveDestination

		private async Task RunProtocolAsync(IOdetteFtpChannel channel, bool initiator)
		{
			// start oftp handling
			var protocol = new OdetteFtp(this, channel);
			try
			{
				await protocol.RunAsync(initiator);
			}
			finally
			{
				await protocol.DisconnectAsync();
			}
		} // func RunProtocolAsync

		private IOdetteFileService FindFileService(string initiatorCode, string password)
		{
			lock (destinations)
				return destinations.FirstOrDefault(c => c.DestinationId == initiatorCode && (c.Password == null || String.Compare(c.Password, password, StringComparison.OrdinalIgnoreCase) == 0));
		} // func FindFileService

		public IEnumerator<IOdetteDestination> GetEnumerator()
		{
			lock (destinations)
			{
				foreach (var cur in destinations)
					yield return cur;
			}
		} // func GetEnumerator
		
		IEnumerator IEnumerable.GetEnumerator()
			=> GetEnumerator();

		public void Start()
		{
			if (listener != null)
				return;

			listener = new TcpListener(ListenLocal ? IPAddress.Loopback : IPAddress.Any, ListenPort);
			listener.Start();

			EndAcceptSocket(null);
			OnPropertyChanged(nameof(IsRunning));
		} // proc Start

		public void Stop()
		{
			if (listener == null)
				return;
			listener.Stop();
			listener = null;
			OnPropertyChanged(nameof(IsRunning));
		} // proc Stop

		private void EndAcceptSocket(IAsyncResult ar)
		{
			if (ar != null)
			{
				var lsnA = (TcpListener)ar.AsyncState;
				Socket s = null;
				try
				{
					s = lsnA.EndAcceptSocket(ar);
				}
				catch { }

				if (s != null)
					RunAsync(s).Silent(RunFalied);
			}

			var lsnN = listener;
			if (lsnN != null)
				lsnN.BeginAcceptSocket(EndAcceptSocket, lsnN);
		} // proc EndAcceptSocket

		private void RunFalied(Exception ex)
			=> AddLog(ex.ToString());

		private async Task RunAsync(Socket s)
		{
			//var ssl = new SslStream(s, false);
			//ssl.AuthenticateAsServerAsync(serverCertificate, true, SslProtocols.Default, true);

			using (var channel = new OdetteNetworkStream(new NetworkStream(s, true), "Listener", null, OdetteCapabilities.Receive | OdetteCapabilities.Send))
			{
				var ftp = new OdetteFtp(this, channel);
				await ftp.RunAsync(false);
			}
		} // proc RunAsync

		public void AddLog(string text)
		{
			void AddLogUI(DataLogLine line)
			{
				while (log.Count > 1000)
					log.RemoveAt(0);
				log.Add(line);
			}

			SynchronizeUI(() => AddLogUI(new DataLogLine(text)));
		} // proc AddLog

		public bool IsDirty => isDirty;
		public string Name => profileName;

		public string OdetteId { get => odetteId; set => Set(ref odetteId, value, nameof(OdetteId)); }
		public string OdettePassword { get => odettePassword; set => Set(ref odettePassword, value, nameof(OdettePassword)); }

		public int ListenPort { get => listenPort; set => Set(ref listenPort, value, nameof(listenPort)); }
		public bool ListenLocal { get => listenLocal; set => Set(ref listenLocal, value, nameof(listenLocal)); }

		public bool IsRunning => listener != null;

		public IEnumerable<DataLogLine> Log => log;

		public string BasePath => profileFileInfo.Directory.FullName;
	} // class Data
}
