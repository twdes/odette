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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Neo.IronLua;
using TecWare.DE.Server;
using TecWare.DE.Server.Configuration;
using TecWare.DE.Stuff;

namespace TecWare.DE.Odette.Services
{
	/// <summary></summary>
	public class DirectoryFileServiceItem : DEConfigLogItem, IOdetteFileServiceFactory
	{
		#region -- class FileItem -----------------------------------------------------

		private sealed class FileItem : IOdetteFileDescription
		{
			private readonly WeakReference<DirectoryFileServiceItem> notifyTarget;

			private readonly FileInfo fileInfo;

			private readonly string virtualFileName;
			private readonly DateTime fileStamp;
			private readonly string originator;

			private readonly OdetteFileFormat format = OdetteFileFormat.Unstructured;
			private readonly int maximumRecordSize;
			private readonly long fileSize = -1;
			private readonly long fileSizeUnpacked = -1;
			private readonly string description = null;

			private readonly XDocument xExtentions;
			private readonly XElement xSendInfo;

			public FileItem(DirectoryFileServiceItem notifyTarget, FileInfo fileInfo, IOdetteFile file, bool createInfo)
			{
				var fileDescription = file as IOdetteFileDescription;

				this.notifyTarget = new WeakReference<DirectoryFileServiceItem>(notifyTarget);
				this.fileInfo = fileInfo;

				this.virtualFileName = file.VirtualFileName;
				this.fileStamp = file.FileStamp;
				this.originator = file.Originator;

				var fiExtended = new FileInfo(GetExtendedFile());
				var readed = false;
				if (!createInfo && fiExtended.Exists)
				{
					// read file
					xExtentions = XDocument.Load(fiExtended.FullName);

					// read description element
					var xDescription = xExtentions.Root.Element("description") ?? new XElement("description");

					// get the attributes
					var stringFormat = xDescription.GetAttribute("format", "U");
					if (String.IsNullOrEmpty(stringFormat))
						format = OdetteFileFormat.Unstructured;
					else
					{
						switch (Char.ToUpper(stringFormat[0]))
						{
							case 'T':
								format = OdetteFileFormat.Text;
								break;
							case 'F':
								format = OdetteFileFormat.Fixed;
								break;
							case 'V':
								format = OdetteFileFormat.Variable;
								break;
							default:
								format = OdetteFileFormat.Unstructured;
								break;
						}
					}

					maximumRecordSize = xDescription.GetAttribute("maximumRecordSize", 0);
					fileSize = xDescription.GetAttribute("fileSize", -1L);
					if (fileSize < 0)
					{
						fileSize = fileInfo.Length / 1024; // 1ks
						if ((fileInfo.Length & 0x3FF) != 0)
							fileSize++;
					}

					fileSizeUnpacked = xDescription.GetAttribute("fileSizeUnpacked", fileSize);
					description = xDescription.Value ?? String.Empty;

					readed = true;
				}

				if (!readed) // create the new info xml
				{
					this.format = fileDescription?.Format ?? OdetteFileFormat.Unstructured;
					this.maximumRecordSize = fileDescription?.MaximumRecordSize ?? 0;
					this.fileSize = fileDescription?.FileSize ?? (fileInfo.Exists ? fileInfo.Length : 0);
					this.fileSizeUnpacked = fileDescription?.FileSizeUnpacked ?? fileSize;
					this.description = fileDescription?.Description ?? String.Empty;

					// create extented attributes
					xExtentions = new XDocument(
						new XDeclaration("1.0", Encoding.Default.WebName, "yes"),
						new XElement("oftp",
							new XElement("description",
								new XAttribute("format", format),
								new XAttribute("maximumRecordSize", maximumRecordSize),
								new XAttribute("fileSize", fileSize),
								new XAttribute("fileSizeUnpacked", fileSizeUnpacked),
								new XText(description)
							)
						)
					);

					if (!fiExtended.Exists)
						xExtentions.Save(fiExtended.FullName);
				}

				// optional information for send
				xSendInfo = xExtentions.Root.Element("send") ?? new XElement("send");
			} // ctor

			public void Log(LoggerProxy log, string firstLine)
			{
				// logging
				log.Info(String.Join(Environment.NewLine,
						firstLine,
						"  Filename: {0}",
						"  Originator: {1}",
						"  VirtualFileName: {2}",
						"  Stamp: {3:F}",
						"  Format: {4}",
						"  MaximumRecordSize: {5:N0}",
						"  FileSize: {6:N0}",
						"  FileSizeUnpacked: {7:N0}",
						"  Description: {8}"
					),
					fileInfo.Name,
					originator,
					virtualFileName,
					fileStamp,
					format,
					maximumRecordSize,
					fileSize,
					fileSizeUnpacked,
					description
				);
			} // proc Log

			private string GetExtendedFile()
				=> Path.ChangeExtension(fileInfo.FullName, ".xml");

			public IOdetteFileWriter OpenWrite()
			{
				switch (format)
				{
					case OdetteFileFormat.Unstructured:
					case OdetteFileFormat.Text:
						return new OdetteFileStreamUnstructured(this, false);
					case OdetteFileFormat.Fixed:
						return new OdetteFileStreamFixed(this, false);
					case OdetteFileFormat.Variable:
						return new OdetteFileStreamVariable(this, false);
					default:
						throw new ArgumentOutOfRangeException(nameof(format));
				}
			} // func OpenWrite

			public IOdetteFileReader OpenRead()
			{
				switch (format)
				{
					case OdetteFileFormat.Unstructured:
					case OdetteFileFormat.Text:
						return new OdetteFileStreamUnstructured(this, true);
					case OdetteFileFormat.Fixed:
						return new OdetteFileStreamFixed(this, true);
					case OdetteFileFormat.Variable:
						return new OdetteFileStreamVariable(this, true);
					default:
						throw new ArgumentOutOfRangeException(nameof(format));
				}
			} // func OpenRead

			public Task SaveExtensionsAsync()
				=> Task.Run(() => xExtentions.Save(GetExtendedFile()));

			public long GetFileSizeSafe()
			{
				try
				{
					fileInfo.Refresh();
					return fileInfo.Length;
				}
				catch
				{
					return 0;
				}
			} // func GetFileSizeSafe

			internal async Task NotifyEndToEndReceivedAsync()
			{
				if (notifyTarget.TryGetTarget(out var nt))
					await nt.OnEndToEndReceivedAsync(this);
			} // proc NotifyEndToEndReceived

			internal async Task NotifyFileReceivedAsync()
			{
				if (notifyTarget.TryGetTarget(out var nt))
					await nt.OnFileReceivedAsync(this);
			} // proc NotifyFileReceived

			public FileInfo FileInfo => fileInfo;
			public XDocument Extensions => xExtentions;

			public string VirtualFileName => virtualFileName;
			public DateTime FileStamp => fileStamp;
			public string Originator => originator;

			public OdetteFileFormat Format => format;
			public int MaximumRecordSize => maximumRecordSize;
			public long FileSize => fileSize;
			public long FileSizeUnpacked => fileSize;
			public string Description => description;

			public string SendUserData => xSendInfo.GetAttribute("userData", String.Empty);
		} // class FileItem

		#endregion

		#region -- class FileEndToEnd -------------------------------------------------

		private sealed class FileEndToEnd : IOdetteFileEndToEnd
		{
			private readonly FileItem item;
			private readonly XElement xCommit;

			public FileEndToEnd(FileItem item)
			{
				this.item = item;

				// parse nerp
				xCommit = item.Extensions.Root.Element("commit") ?? new XElement("commit");
			} // ctor

			public async Task CommitAsync()
			{
				await ChangeInFileStateAsync(item.FileInfo, OdetteInFileState.Finished);
				await item.NotifyEndToEndReceivedAsync();
			} // proc Commit

			public IOdetteFile Name => item;

			public int ReasonCode => xCommit.GetAttribute("reasonCode", 0);
			public string ReasonText => xCommit.GetAttribute("reasonText", String.Empty);
			public string UserData => xCommit.GetAttribute("userData", String.Empty);
		} // class FileEndToEnd 

		#endregion

		#region -- class OdetteFileStream ---------------------------------------------

		private abstract class OdetteFileStream : IOdetteFileWriter, IOdetteFileReader, IOdetteFilePosition, IDisposable
		{
			private readonly FileItem fileItem;
			private readonly bool readOnly;
			private Stream stream = null;

			#region -- Ctor/Dtor ------------------------------------------------------

			public OdetteFileStream(FileItem fileItem, bool readOnly)
			{
				this.fileItem = fileItem;
				this.readOnly = readOnly;

				stream = readOnly ?
					fileItem.FileInfo.OpenRead() :
					fileItem.FileInfo.OpenWrite();
			} // ctor

			public void Dispose()
			{
				Dispose(true);
			} // proc Dispose

			protected virtual void Dispose(bool disposing)
			{
				if (disposing)
					Procs.FreeAndNil(ref stream);
			} // prop Dispsoe

			#endregion

			#region -- Write/Read/Seek ------------------------------------------------

			public Task WriteAsync(byte[] buf, int offset, int count, bool isEoR)
			{
				if (readOnly)
					throw new InvalidOperationException();
				if (stream == null)
					throw new ArgumentNullException(nameof(stream));

				return WriteInternAsync(buf, offset, count, isEoR);
			} // proc WriteAsync

			protected abstract Task WriteInternAsync(byte[] buf, int offset, int count, bool isEoR);

			public Task<(int readed, bool isEoR)> ReadAsync(byte[] buf, int offset, int count)
			{
				if (!readOnly)
					throw new InvalidOperationException();
				if (stream == null)
					throw new ArgumentNullException("stream");

				return ReadInternAsync(buf, offset, count);
			} // func ReadAsync

			protected abstract Task<(int readed, bool isEoR)> ReadInternAsync(byte[] buf, int offset, int count);

			public abstract Task<long> SeekAsync(long position);

			protected async Task<long> TruncateAsync(long readPosition)
			{
				if (!readOnly)
				{
					if (readPosition < stream.Length)
						await Task.Run(() => stream.SetLength(readPosition)); // truncate current data
				}
				return readPosition;
			} // func TruncateAsync

			#endregion

			#region -- Commit/Transmission State --------------------------------------

			public async Task CommitFileAsync(long recordCount, long unitCount)
			{
				if (readOnly)
					throw new InvalidOperationException();

				// validate file size
				if (RecordCount != recordCount)
					throw new OdetteFileServiceException(OdetteAnswerReason.InvalidRecordCount, String.Format("Invalid record count (local: {0}, endpoint: {1}).", RecordCount, recordCount));
				if (TotalLength != unitCount)
					throw new OdetteFileServiceException(OdetteAnswerReason.InvalidByteCount, String.Format("Invalid record count (local: {0}, endpoint: {1}).", TotalLength, unitCount));

				// close the stream
				Procs.FreeAndNil(ref stream);

				// rename file to show that it is received
				await ChangeInFileStateAsync(fileItem.FileInfo, OdetteInFileState.Received);

				// notify that the file is received
				await fileItem.NotifyFileReceivedAsync();
			} // proc CommitFileAsync

			private XElement EnforceSendElement()
			{
				var xSend = fileItem.Extensions.Root.Element("send");
				if (xSend == null)
					fileItem.Extensions.Root.Add(xSend = new XElement("send"));
				return xSend;
			} // func EnforceSendElement

			public async Task SetTransmissionErrorAsync(OdetteAnswerReason answerReason, string reasonText, bool retryFlag)
			{
				if (!readOnly)
					throw new InvalidOperationException();

				// close the stream
				Procs.FreeAndNil(ref stream);

				// write answer
				var xSend = EnforceSendElement();

				xSend.SetAttributeValue("reasonCode", (int)answerReason);
				xSend.SetAttributeValue("reasonText", reasonText);

				await fileItem.SaveExtensionsAsync();

				// no retry, mark as finished
				if (!retryFlag)
					await ChangeOutFileStateAsync(fileItem.FileInfo, OdetteOutFileState.Finished);
			} // proc SetTransmissionErrorAsync

			public async Task SetTransmissionStateAsync()
			{
				if (!readOnly)
					throw new InvalidOperationException();

				// close the stream
				Procs.FreeAndNil(ref stream);

				// clear answer state to successful
				var xSend = EnforceSendElement();

				xSend.SetAttributeValue("reasonCode", 0);
				xSend.SetAttributeValue("reasonText", String.Empty);

				await fileItem.SaveExtensionsAsync();

				// chanhe state
				await ChangeOutFileStateAsync(fileItem.FileInfo, OdetteOutFileState.WaitEndToEnd);
			} // proc ChangeOutFileStateAsync

			#endregion

			protected Stream Stream => stream;
			protected bool IsReadOnly => readOnly;
			protected FileItem FileItem => fileItem;

			public IOdetteFile Name => fileItem;

			public long TotalLength => stream?.Length ?? fileItem.GetFileSizeSafe();
			public virtual long RecordCount => 0;

			string IOdetteFileReader.UserData => fileItem.SendUserData;
		} // class OdetteFileStream

		#endregion

		#region -- class OdetteFileStreamUnstructured ---------------------------------

		/// <summary>Controls text and unstructured files.</summary>
		private sealed class OdetteFileStreamUnstructured : OdetteFileStream
		{
			public OdetteFileStreamUnstructured(FileItem fileItem, bool readOnly)
				: base(fileItem, readOnly)
			{
			} // ctor

			protected override Task WriteInternAsync(byte[] buf, int offset, int count, bool isEof)
				=> Stream.WriteAsync(buf, offset, count);

			protected override async Task<(int readed, bool isEoR)> ReadInternAsync(byte[] buf, int offset, int count)
			{
				var readed = await Stream.ReadAsync(buf, offset, count);
				if (readed == 0)
					readed = -1; // enforced eof

				return (readed, readed < count);
			} // func ReadInternAsync

			public override async Task<long> SeekAsync(long position)
			{
				var newpos = position << 10;
				if (newpos > Stream.Length)
					newpos = Stream.Length & ~0x3FF;

				return await TruncateAsync(Stream.Seek(newpos, SeekOrigin.Begin)) >> 10;
			} // func SeekAsync
		} // class OdetteFileStreamUnstructured

		#endregion

		#region -- class OdetteFileStreamFixed ----------------------------------------

		/// <summary>Controls fixed record length streams.</summary>
		private sealed class OdetteFileStreamFixed : OdetteFileStream
		{
			private readonly int recordSize;
			private int recordOffset = 0;

			public OdetteFileStreamFixed(FileItem fileItem, bool readOnly)
				: base(fileItem, readOnly)
			{
				this.recordSize = fileItem.MaximumRecordSize;
			} // ctor

			protected override async Task WriteInternAsync(byte[] buf, int offset, int count, bool isEoR)
			{
				recordOffset += count;
				await Stream.WriteAsync(buf, offset, count);
				if (isEoR)
				{
					if (recordOffset != recordSize)
						throw new OdetteFileServiceException(OdetteAnswerReason.UnspecifiedReason, String.Format("Invalid record size (expected {0} but {1} bytes received).", recordSize, recordOffset));
					recordOffset = 0;
				}
			} // proc WriteInternAsync

			protected override async Task<(int readed, bool isEoR)> ReadInternAsync(byte[] buf, int offset, int count)
			{
				var restRecord = recordSize - recordOffset;

				// read bytes
				count = count < restRecord ? count : restRecord;
				var r = await Stream.ReadAsync(buf, offset, count);
				recordOffset += r;

				// check eor
				var isEoR = recordOffset >= recordSize;
				if (isEoR)
					recordOffset = 0;

				return (r, isEoR);
			} // func ReadInternAsync

			public override async Task<long> SeekAsync(long position)
			{
				var newpos = position * recordSize;
				if (newpos > Stream.Length)
					newpos = Stream.Length / recordSize * recordSize;
				return await TruncateAsync(Stream.Seek(newpos, SeekOrigin.Begin)) / recordSize;
			} // func SeekAsync

			public override long RecordCount => TotalLength / recordSize;
		} // class OdetteFileStreamFixed

		#endregion

		#region -- class OdetteFileStreamVariable -------------------------------------

		/// <summary>Controls variable record length streams.</summary>
		private sealed class OdetteFileStreamVariable : OdetteFileStream
		{
			private readonly int maximumRecordSize;
			private int currentRecord = 0;
			private int recordOffset = 0;
			private List<Tuple<long, int>> records = new List<Tuple<long, int>>();

			public OdetteFileStreamVariable(FileItem fileItem, bool readOnly)
				: base(fileItem, readOnly)
			{
				this.maximumRecordSize = fileItem.MaximumRecordSize;

				if (readOnly) // get record structure
				{
					var xRecord = FileItem.Extensions.Root.Element("records");
					if (xRecord != null)
					{
						// read records
						foreach (var x in xRecord.Elements("r"))
						{
							var offset = x.GetAttribute("o", -1L);
							var length = x.GetAttribute("l", -1);
							if (offset == -1 || length == -1)
								throw new ArgumentException("Invalid record defined.");
							records.Add(new Tuple<long, int>(offset, length));
						}

						// check sort
						records.Sort((a, b) => a.Item1.CompareTo(b.Item1));
					}
				}
			} // ctor

			protected override void Dispose(bool disposing)
			{
				if (disposing && !IsReadOnly)
				{
					// clear records
					var xRecord = FileItem.Extensions.Root.Element("records");
					if (xRecord == null)
						FileItem.Extensions.Root.Add(xRecord = new XElement("records"));
					else
						xRecord.RemoveAll();

					// write records
					xRecord.Add(
						from t in records
						select new XElement("r", new XAttribute("o", t.Item1), new XAttribute("l", t.Item2))
					);

					FileItem.SaveExtensionsAsync();
				}
				base.Dispose(disposing);
			} // proc Dispose

			protected override async Task WriteInternAsync(byte[] buf, int offset, int count, bool isEoR)
			{
				// write data to file
				await Stream.WriteAsync(buf, offset, count);
				recordOffset += count;

				if (recordOffset > maximumRecordSize) // check size
					throw new OdetteFileServiceException(OdetteAnswerReason.UnspecifiedReason, String.Format("Invalid record size (maximum expected {0} but {1} bytes received).", maximumRecordSize, recordOffset));
				else if (isEoR) // add the record description
				{
					records.Add(new Tuple<long, int>(Stream.Position - recordOffset, recordOffset));
					recordOffset = 0;
				}
			} // proc WriteIntern

			protected override async Task<(int readed, bool isEoR)> ReadInternAsync(byte[] buf, int offset, int count)
			{
				if (currentRecord < records.Count)
				{
					if (recordOffset == 0) // reset file position
					{
						var tmp = records[currentRecord].Item1;
						Stream.Seek(tmp, SeekOrigin.Begin);
						if (records[currentRecord].Item2 > maximumRecordSize)
							throw new OdetteFileServiceException(OdetteAnswerReason.UnspecifiedReason, String.Format("Invalid record size (maximum expected {0} but {1} bytes received).", maximumRecordSize, tmp));
					}

					var recordLength = records[currentRecord].Item2;
					var restRecord = recordLength - recordOffset;

					// read bytes
					count = count < restRecord ? count : restRecord;
					var r = await Stream.ReadAsync(buf, offset, count);
					recordOffset += r;

					// check eor
					var isEoR = recordOffset >= recordLength;
					if (isEoR)
					{
						currentRecord++;
						recordOffset = 0;
					}
					return (r, isEoR);
				}
				else
					return (-1, true);
			} // func ReadIntern

			public override Task<long> SeekAsync(long position)
			{
				if (position >= RecordCount)
					position = RecordCount;

				if (currentRecord != position)
				{
					currentRecord = (int)position;
					recordOffset = 0;
				}

				return Task.FromResult<long>(currentRecord);
			} // func Seek

			public override long RecordCount => records.Count;
		} // class OdetteFileStreamVariable

		#endregion

		#region -- class FileServiceSession -------------------------------------------

		private sealed class FileServiceSession : IOdetteFileService2
		{
			private readonly int sessionId;
			private readonly DirectoryFileServiceItem service;
			private readonly LoggerProxy log;

			public FileServiceSession(DirectoryFileServiceItem service, int sessionId)
			{
				this.sessionId = sessionId;
				this.service = service;
				this.log = LoggerProxy.Create(service.Log, sessionId.ToString());

				log.Info("Session started...");
				service.OnSessionStart();
			} // ctor

			public void Dispose()
			{
				service.OnSessionClosed();
				log.Info("Session finished...");
			} // proc Dispose

			public async Task<IOdetteFileWriter> CreateInFileAsync(IOdetteFile file, string userData)
			{
				var incomingFile = String.Format("In coming file {0} ", OdetteFileImmutable.FormatFileName(file, userData));
				if (!service.IsInFileAllowed(file))
				{
					log.Info(incomingFile + "ignored");
					return null;
				}

				var fi = service.CreateInFileName(file, OdetteInFileState.Pending);

				// check if the file exists
				if (File.Exists(Path.ChangeExtension(fi.FullName, GetInFileExtention(OdetteInFileState.Received))) ||
					File.Exists(Path.ChangeExtension(fi.FullName, GetInFileExtention(OdetteInFileState.PendingEndToEnd))) ||
					File.Exists(Path.ChangeExtension(fi.FullName, GetInFileExtention(OdetteInFileState.Finished))))
					throw new OdetteFileServiceException(OdetteAnswerReason.DuplicateFile, "File already exists.", false);

				// open the file to write
				var fileItem = await Task.Run(() => new FileItem(service, fi, file, true));
				fileItem.Log(log, incomingFile + "accepted");
				try
				{
					return await Task.Run(new Func<IOdetteFileWriter>(fileItem.OpenWrite));
				}
				catch (IOException e)
				{
					throw new OdetteFileServiceException(OdetteAnswerReason.UnspecifiedReason, e.Message, false, e);
				}
			} // func CreateInFile

			public IEnumerable<IOdetteFileEndToEnd> GetEndToEnd()
			{
				if (service.directoryIn == null) // do we have the directory
					yield break;

				// enumerator all files
				foreach (var fi in service.directoryIn.EnumerateFiles("*" + GetInFileExtention(OdetteInFileState.PendingEndToEnd), SearchOption.TopDirectoryOnly))
				{
					var file = TrySplitFileName(fi.Name);
					if (file != null)
					{
						var fileItem = new FileItem(service, fi, file, false);
						var e2e = new FileEndToEnd(fileItem);
						fileItem.Log(log, String.Format("Sent {1} end to end for: {0}", OdetteFileImmutable.FormatFileName(file, e2e.UserData), e2e.ReasonCode == 0 ? "positive" : "negative"));
						yield return e2e;
					}
					else
						log.Warn("GetEndToEnd: Can not parse file name '{0}'...", fi.Name);
				}
			} // func GetEndToEnd

			public IEnumerable<Func<IOdetteFileReader>> GetOutFiles()
			{
				if (service.directoryOut == null) // do we have the directory
					yield break;

				// collect alle out files
				foreach (var fi in service.directoryOut.EnumerateFiles("*" + GetOutFileExtention(OdetteOutFileState.Sent)))
				{
					var file = TrySplitFileName(fi.Name);
					if (file != null)
						yield return new Func<IOdetteFileReader>(() =>
						{
							var fileItem = new FileItem(service, fi, file, false);
							fileItem.Log(log, String.Format("Sent file to destination: {0}", OdetteFileImmutable.FormatFileName(file, fileItem.SendUserData)));

							// file for sent
							return fileItem.OpenRead();
						});
				}
			} // func GetOutFiles

			public async Task<bool> UpdateOutFileStateAsync(IOdetteFileEndToEndDescription description)
			{
				if (service.directoryOut == null) // do we have the directory
					return false;

				// check file exists
				var fi = service.CreateOutFileName(description.Name, OdetteOutFileState.WaitEndToEnd);
				if (!fi.Exists)
					return false;

				// mark file as finish
				await ChangeOutFileStateAsync(fi, OdetteOutFileState.ReceivedEndToEnd);

				// update file information
				var fileItem = await Task.Run(() => new FileItem(service, fi, description.Name, false));
				fileItem.Log(log, String.Format("Update file commit: {0} with [{1}] {2}", OdetteFileImmutable.FormatFileName(description.Name, description.UserData), description.ReasonCode, description.ReasonText));

				var xCommit = fileItem.Extensions.Root.Element("commit");
				if (xCommit == null)
					fileItem.Extensions.Root.Add(xCommit = new XElement("commit"));
				
				xCommit.SetAttributeValue("reasonCode", description.ReasonCode);
				xCommit.SetAttributeValue("reasonText", description.ReasonText);
				xCommit.SetAttributeValue("userData", description.UserData);

				await fileItem.SaveExtensionsAsync();

				return true;
			} // proc UpdateOutFileState

			public string DestinationId => service.destinationId;
			public int Priority => service.priority;

			public bool SupportsInFiles => service.directoryIn != null;
			public bool SupportsOutFiles => service.directoryOut != null;
		} // class FileServiceSession

		#endregion

		internal const string fileSelectorRegEx = @"^([A-Za-z0-9\s\-]{1,25})#([A-Za-z0-9_\/\-\.\&\(\)\s]{1,26})#(\d{18})\.?.*";

		private const string fileStampFormat = "yyyyMMddHHmmssffff";

		private DirectoryInfo directoryIn = null;
		private DirectoryInfo directoryOut = null;

		private string destinationId;
		private string[] fileNameFilter = null;
		private int priority;
		private int lastSessionId = 0;

		#region -- Ctor/Dtor/Config ---------------------------------------------------

		/// <summary></summary>
		/// <param name="sp"></param>
		/// <param name="name"></param>
		public DirectoryFileServiceItem(IServiceProvider sp, string name)
			: base(sp, name)
		{
		} // ctor

		/// <summary></summary>
		/// <param name="config"></param>
		protected override void OnBeginReadConfiguration(IDEConfigLoading config)
		{
			base.OnBeginReadConfiguration(config);

			// check the id's
			if (String.IsNullOrEmpty(config.ConfigNew.GetAttribute("destination", String.Empty)))
				throw new ArgumentNullException("@destination id is missing.");

			// check the directories
			ValidateDirectory(config.ConfigNew, "in", true);
			ValidateDirectory(config.ConfigNew, "out", true);
		} // OnBeginReadConfiguration

		/// <summary></summary>
		/// <param name="config"></param>
		protected override void OnEndReadConfiguration(IDEConfigLoading config)
		{
			base.OnEndReadConfiguration(config);

			// read the attributes
			var x = XConfigNode.Create(Server.Configuration, config.ConfigNew);

			destinationId = x.GetAttribute<string>("destination").ToUpper();
			priority = x.GetAttribute<int>("priority");

			fileNameFilter = x.GetAttribute<string[]>("inFilter");

			// set directories
			directoryIn = x.GetAttribute<DirectoryInfo>("in");
			directoryOut = x.GetAttribute<DirectoryInfo>("out");
		} // proc OnEndReadConfiguration

		#endregion

		#region -- CreateFileService Session ------------------------------------------

		private int GetSessionId()
			=> Interlocked.Increment(ref lastSessionId);

		IOdetteFileService2 IOdetteFileServiceFactory.CreateFileService(string destinationId, string password)
		{
			if (destinationId == this.destinationId) // case sensitive
			{
				// check the password
				var passwordHash = Config.GetAttribute("passwordHash", null);
				if (passwordHash == null)
				{
					if (!String.IsNullOrEmpty(password))
						Log.Warn("Password is empty, but a password is transmitted.");
				}
				else if (!ProcsDE.PasswordCompare(password, passwordHash))
				{
					Log.Warn("Wrong password for asked destination.");
					return null;
				}

				return new FileServiceSession(this, GetSessionId());
			}
			else
				return null;
		} // func CreateFileService

		#endregion

		#region -- Script Notify ------------------------------------------------------

		private void OnSessionStart()
		{
			try
			{
				var m = this[nameof(OnSessionStart)];
				if (m != null && Lua.RtInvokeable(m))
					CallMember(nameof(OnSessionStart));
			}
			catch (Exception e)
			{
				Log.Except(e);
			}
		} // proc OnSessionStart

		private void OnSessionClosed()
		{
			try
			{
				var m = this[nameof(OnSessionClosed)];
				if (m != null && Lua.RtInvokeable(m))
					CallMember(nameof(OnSessionClosed));
			}
			catch (Exception e)
			{
				Log.Except(e);
			}
		} // proc OnSessionStart

		private Task OnFileReceivedAsync(FileItem fileItem)
			=> CallFileItemNotifyAsync("OnFileReceived", fileItem);

		private Task OnEndToEndReceivedAsync(FileItem fileItem)
			=> CallFileItemNotifyAsync("OnEndToEndReceived", fileItem);

		private async Task CallFileItemNotifyAsync(string methodName, FileItem fileItem)
		{
			var m = this[methodName];
			if (m != null && Lua.RtInvokeable(m))
			{
				try
				{
					await Task.Run(() => CallMember(methodName, fileItem, fileItem.FileInfo));
					Log.Info("{0} successful.", methodName);
				}
				catch (Exception e)
				{
					Log.Except(String.Format("{0} failed.", methodName), e);
				}
			}
		} // proc CallFileItemNotify

		#endregion

		#region -- Directory Helper ---------------------------------------------------

		/// <summary>Checks if the file is receivable.</summary>
		/// <param name="fileDescription"></param>
		/// <returns></returns>
		internal bool IsInFileAllowed(IOdetteFile fileDescription)
		{
			if (directoryIn == null)
				return false;

			if (fileNameFilter == null || fileNameFilter.Length == 0)
				return true;

			foreach (var cur in fileNameFilter)
			{
				if (Procs.IsFilterEqual(fileDescription.VirtualFileName, cur))
					return true;
			}

			return false;
		} // func IsInFileAllowed

		/// <summary>Creates the file name, for the description, without state information.</summary>
		/// <param name="fileDescription"></param>
		/// <returns></returns>
		private static string GetFileName(IOdetteFile fileDescription)
		{
			if (fileDescription == null)
				throw new ArgumentNullException(nameof(fileDescription));
			if (String.IsNullOrEmpty(fileDescription.VirtualFileName))
				throw new ArgumentNullException(nameof(IOdetteFile.VirtualFileName));
			if (fileDescription.FileStamp == null)
				throw new ArgumentNullException(nameof(IOdetteFile.FileStamp));

			return fileDescription.Originator + "#" +
				fileDescription.VirtualFileName + "#" +
				fileDescription.FileStamp.ToString(fileStampFormat, CultureInfo.InvariantCulture);
		} // func GetFileName

		private static IOdetteFile TrySplitFileName(string name)
		{
			if (String.IsNullOrEmpty(name))
				throw new ArgumentNullException("name");

			var m = Regex.Match(name, fileSelectorRegEx);
			return m.Success && DateTime.TryParseExact(m.Groups[3].Value, fileStampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fileStamp)
				? new OdetteFileImmutable(m.Groups[2].Value, fileStamp, m.Groups[1].Value)
				: null;
		} // func TrySplitFileName

		private static string GetInFileExtention(OdetteInFileState state)
		{
			switch (state)
			{
				case OdetteInFileState.Pending:
					return ".new";
				case OdetteInFileState.Received:
					return ".recv";
				case OdetteInFileState.PendingEndToEnd:
					return ".se2e";
				case OdetteInFileState.Finished:
					return ".done";
				default:
					throw new ArgumentException("Invalid state.");
			}
		} // func GetInFileExtention

		private static string GetOutFileExtention(OdetteOutFileState state)
		{
			switch (state)
			{
				case OdetteOutFileState.Sent:
					return ".sent";
				case OdetteOutFileState.WaitEndToEnd:
					return ".we2e";
				case OdetteOutFileState.ReceivedEndToEnd:
					return ".re2e";
				case OdetteOutFileState.Finished:
					return ".done";
				default:
					throw new ArgumentException("Invalid state.");
			}
		} // func GetInFileExtention

		/// <summary>Builds the file name with state extention.</summary>
		/// <param name="file"></param>
		/// <param name="state"></param>
		/// <returns></returns>
		internal FileInfo CreateInFileName(IOdetteFile file, OdetteInFileState state)
			=> new FileInfo(Path.Combine(directoryIn.FullName, GetFileName(file) + GetInFileExtention(state)));

		/// <summary>Builds the file name with state extention.</summary>
		/// <param name="file"></param>
		/// <param name="state"></param>
		/// <returns></returns>
		internal FileInfo CreateOutFileName(IOdetteFile file, OdetteOutFileState state)
			=> new FileInfo(Path.Combine(directoryOut.FullName, GetFileName(file) + GetOutFileExtention(state)));

		private static Task ChangeInFileStateAsync(FileInfo fileInfo, OdetteInFileState newState)
		{
			var fiNewFileName = Path.ChangeExtension(fileInfo.FullName, GetInFileExtention(newState));
			return Task.Run(() => fileInfo.MoveTo(fiNewFileName));
		} // func ChangeInFileStateAsync

		private static Task ChangeOutFileStateAsync(FileInfo fileInfo, OdetteOutFileState newState)
		{
			var fiNewFileName = Path.ChangeExtension(fileInfo.FullName, GetOutFileExtention(newState));
			return Task.Run(() => fileInfo.MoveTo(fiNewFileName));
		} // func ChangeOutFileStateAsync

		#endregion
	} // class DirectoryFileServiceItem
}
