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
using TecWare.DE.Server;
using TecWare.DE.Server.Configuration;
using TecWare.DE.Stuff;

namespace TecWare.DE.Odette.Services
{
	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public class DirectoryFileServiceItem : DEConfigLogItem, IOdetteFileServiceFactory
	{
		#region -- class FileItem ---------------------------------------------------------

		///////////////////////////////////////////////////////////////////////////////
		/// <summary></summary>
		private sealed class FileItem : IOdetteFileDescription
		{
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

			public FileItem(FileInfo fileInfo, IOdetteFile file, bool createInfo)
			{
				var fileDescription = file as IOdetteFileDescription;

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
					if (string.IsNullOrEmpty(stringFormat))
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
						throw new ArgumentOutOfRangeException("format");
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
						throw new ArgumentOutOfRangeException("format");
				}
			} // func OpenRead

			public void SaveExtensions()
			{
				xExtentions.Save(GetExtendedFile());
			} // proc SaveExtensions

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

		#region -- class FileEndToEnd -----------------------------------------------------

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

			public void Commit()
			{
				ChangeInFileState(item.FileInfo, OdetteInFileState.Finished);
			} // proc Commit

			public IOdetteFile Name => item;

			public int ReasonCode => xCommit.GetAttribute("reasonCode", 0);
			public string ReasonText => xCommit.GetAttribute("reasonText", String.Empty);
			public string UserData => xCommit.GetAttribute("userData", String.Empty);
		} // class FileEndToEnd 

		#endregion

		#region -- class OdetteFileStream -------------------------------------------------

		private abstract class OdetteFileStream : IOdetteFileWriter, IOdetteFileReader, IOdetteFilePosition, IDisposable
		{
			private readonly FileItem fileItem;
			private readonly bool readOnly;
			private FileStream stream = null;

			#region -- Ctor/Dtor ------------------------------------------------------------

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

			#region -- Write/Read/Seek ------------------------------------------------------

			public void Write(byte[] buf, int offset, int count, bool isEoR)
			{
				if (readOnly)
					throw new InvalidOperationException();
				if (stream == null)
					throw new ArgumentNullException("stream");

				WriteIntern(buf, offset, count, isEoR);
			} // proc Write

			protected abstract void WriteIntern(byte[] buf, int offset, int count, bool isEoR);

			public int Read(byte[] buf, int offset, int count, out bool isEoR)
			{
				if (!readOnly)
					throw new InvalidOperationException();
				if (stream == null)
					throw new ArgumentNullException("stream");

				return ReadIntern(buf, offset, count, out isEoR);
			} // func Read

			protected abstract int ReadIntern(byte[] buf, int offset, int count, out bool isEoR);

			public abstract long Seek(long position);

			protected long Truncate(long readPosition)
			{
				if (!readOnly)
				{
					if (readPosition < stream.Length)
						stream.SetLength(readPosition); // truncate current data
				}
				return readPosition;
			} // func Truncate

			#endregion

			#region -- Commit/Transmission State --------------------------------------------

			public void CommitFile(long recordCount, long unitCount)
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
				ChangeInFileState(fileItem.FileInfo, OdetteInFileState.Received);
			} // proc CommitFile

			private XElement EnforceSendElement()
			{
				var xSend = fileItem.Extensions.Root.Element("send");
				if (xSend == null)
					fileItem.Extensions.Root.Add(xSend = new XElement("send"));
				return xSend;
			} // func EnforceSendElement

			public void SetTransmissionError(OdetteAnswerReason answerReason, string reasonText, bool retryFlag)
			{
				if (!readOnly)
					throw new InvalidOperationException();

				// close the stream
				Procs.FreeAndNil(ref stream);

				// write answer
				var xSend = EnforceSendElement();

				xSend.SetAttributeValue("reasonCode", (int)answerReason);
				xSend.SetAttributeValue("reasonText", reasonText);

				fileItem.SaveExtensions();

				// no retry, mark as finished
				if (!retryFlag)
					ChangeOutFileState(fileItem.FileInfo, OdetteOutFileState.Finished);
			} // proc SetTransmissionError

			public void SetTransmissionState()
			{
				if (!readOnly)
					throw new InvalidOperationException();

				// close the stream
				Procs.FreeAndNil(ref stream);

				// clear answer state to successful
				var xSend = EnforceSendElement();

				xSend.SetAttributeValue("reasonCode", 0);
				xSend.SetAttributeValue("reasonText", String.Empty);

				fileItem.SaveExtensions();

				// chanhe state
				ChangeOutFileState(fileItem.FileInfo, OdetteOutFileState.WaitEndToEnd);
			} // proc SetTransmissionState

			#endregion

			protected FileStream Stream => stream;
			protected bool IsReadOnly => readOnly;
			protected FileItem FileItem => FileItem;

			public IOdetteFile Name => fileItem;

			public long TotalLength => stream.Length;
			public virtual long RecordCount => 0;

			string IOdetteFileReader.UserData => fileItem.SendUserData;
		} // class OdetteFileStream

		#endregion

		#region -- class OdetteFileStreamUnstructured -------------------------------------

		///////////////////////////////////////////////////////////////////////////////
		/// <summary>Controls text and unstructured files.</summary>
		private sealed class OdetteFileStreamUnstructured : OdetteFileStream
		{
			public OdetteFileStreamUnstructured(FileItem fileItem, bool readOnly)
				: base(fileItem, readOnly)
			{
			} // ctor

			protected override void WriteIntern(byte[] buf, int offset, int count, bool isEof)
			{
				Stream.Write(buf, offset, count);
			} // proc WriteIntern

			protected override int ReadIntern(byte[] buf, int offset, int count, out bool isEoR)
			{
				var readed = Stream.Read(buf, offset, count);
				if (readed == 0)
					readed = -1; // enforced eof
				isEoR = readed < count;
				return readed;
			} // func ReadIntern

			public override long Seek(long position)
			{
				var newpos = position << 10;
				if (newpos > Stream.Length)
					newpos = Stream.Length & ~0x3FF;

				return Truncate(Stream.Seek(newpos, SeekOrigin.Begin)) >> 10;
			} // func Seek
		} // class OdetteFileStreamUnstructured

		#endregion

		#region -- class OdetteFileStreamFixed --------------------------------------------

		///////////////////////////////////////////////////////////////////////////////
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

			protected override void WriteIntern(byte[] buf, int offset, int count, bool isEoR)
			{
				recordOffset += count;
				Stream.Write(buf, offset, count);
				if (isEoR)
				{
					if (recordOffset != recordSize)
						throw new OdetteFileServiceException(OdetteAnswerReason.UnspecifiedReason, String.Format("Invalid record size (expected {0} but {1} bytes received).", recordSize, recordOffset));
					recordOffset = 0;
				}
			} // proc WriteIntern

			protected override int ReadIntern(byte[] buf, int offset, int count, out bool isEoR)
			{
				var restRecord = recordSize - recordOffset;

				// read bytes
				count = count < restRecord ? count : restRecord;
				var r = Stream.Read(buf, offset, count);
				recordOffset += r;

				// check eor
				isEoR = recordOffset >= recordSize;
				if (isEoR)
					recordOffset = 0;

				return r;
			} // func ReadIntern

			public override long Seek(long position)
			{
				var newpos = position * recordSize;
				if (newpos > Stream.Length)
					newpos = Stream.Length / recordSize * recordSize;
				return Truncate(Stream.Seek(newpos, SeekOrigin.Begin)) / recordSize;
			} // func Seek

			public override long RecordCount => TotalLength / recordSize;
		} // class OdetteFileStreamFixed

		#endregion

		#region -- class OdetteFileStreamVariable -----------------------------------------

		///////////////////////////////////////////////////////////////////////////////
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

					FileItem.SaveExtensions();
				}
				base.Dispose(disposing);
			} // proc Dispose

			protected override void WriteIntern(byte[] buf, int offset, int count, bool isEoR)
			{
				// write data to file
				Stream.Write(buf, offset, count);
				recordOffset += count;

				if (recordOffset > maximumRecordSize) // check size
					throw new OdetteFileServiceException(OdetteAnswerReason.UnspecifiedReason, String.Format("Invalid record size (maximum expected {0} but {1} bytes received).", maximumRecordSize, recordOffset));
				else if (isEoR) // add the record description
				{
					records.Add(new Tuple<long, int>(Stream.Position - recordOffset, recordOffset));
					recordOffset = 0;
				}
			} // proc WriteIntern

			protected override int ReadIntern(byte[] buf, int offset, int count, out bool isEoR)
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
					var r = Stream.Read(buf, offset, count);
					recordOffset += r;

					// check eor
					isEoR = recordOffset >= recordLength;
					if (isEoR)
					{
						currentRecord++;
						recordOffset = 0;
					}
					return r;
				}
				else
				{
					isEoR = true;
					return -1;
				}
			} // func ReadIntern

			public override long Seek(long position)
			{
				if (position >= RecordCount)
					position = RecordCount;

				if (currentRecord != position)
				{
					currentRecord = (int)position;
					recordOffset = 0;
				}

				return currentRecord;
			} // func Seek

			public override long RecordCount => records.Count;
		} // class OdetteFileStreamVariable

		#endregion

		#region -- class FileServiceSession -----------------------------------------------

		///////////////////////////////////////////////////////////////////////////////
		/// <summary></summary>
		private sealed class FileServiceSession : IOdetteFileService
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
			} // ctor

			public void Dispose()
			{
				log.Info("Session finished...");
			} // proc Dispose

			private string FormatFileName(IOdetteFile file, string userData)
				=> file.Originator + "/" + file.VirtualFileName + "[userData=" + userData + "]";

			public IOdetteFileWriter CreateInFile(IOdetteFile file, string userData)
			{
				var incomingFile = String.Format("In coming file {0} ", FormatFileName(file, userData));
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
				var fileItem = new FileItem(fi, file, true);
				fileItem.Log(log, incomingFile + "accepted");
				try
				{
					return fileItem.OpenWrite();
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
						var fileItem = new FileItem(fi, file, false);
						var e2e = new FileEndToEnd(fileItem);
						fileItem.Log(log, String.Format("Sent {1} end to end for: {0}", FormatFileName(file, e2e.UserData), e2e.ReasonCode == 0 ? "positive" : "negative"));
						yield return e2e;
					}
				}
			} // func GetEndToEnd

			public IEnumerable<Func<IOdetteFileReader>> GetOutFiles()
			{
				if (service.directoryOut == null) // do we have the directory
					yield break;

				// collect alle out files
				foreach (var fi in service.directoryOut.GetFiles("*" + GetOutFileExtention(OdetteOutFileState.Sent)))
				{
					var file = TrySplitFileName(fi.Name);
					if (file != null)
						yield return new Func<IOdetteFileReader>(() =>
						{
							var fileItem = new FileItem(fi, file, false);
							fileItem.Log(log, String.Format("Sent file to destination: {0}", FormatFileName(file, fileItem.SendUserData)));

							// file for sent
							return fileItem.OpenRead();
						});
				}
			} // func GetOutFiles

			public bool UpdateOutFileState(IOdetteFileEndToEndDescription description)
			{
				if (service.directoryOut == null) // do we have the directory
					return false;

				// check file exists
				var fi = service.CreateOutFileName(description.Name, OdetteOutFileState.WaitEndToEnd);
				if (!fi.Exists)
					return false;

				// mark file as finish
				ChangeOutFileState(fi, OdetteOutFileState.ReceivedEndToEnd);

				// update file information
				var fileItem = new FileItem(fi, description.Name, false);
				fileItem.Log(log, String.Format("Update file commit: {0} with [{1}] {2}", FormatFileName(description.Name, description.UserData), description.ReasonCode, description.ReasonText));

				var xCommit = fileItem.Extensions.Root.Element("commit");
				if (xCommit == null)
					fileItem.Extensions.Root.Add(xCommit = new XElement("commit"));

				xCommit.Add(new XAttribute("reasonCode", description.ReasonCode));
				xCommit.Add(new XAttribute("reasonText", description.ReasonText));
				xCommit.Add(new XAttribute("userData", description.UserData));

				fileItem.SaveExtensions();

				return true;
			} // proc UpdateOutFileState

			public string DestinationId => service.destinationId;
			public int Priority => service.priority;

			public bool SupportsInFiles => service.directoryIn != null;
			public bool SupportsOutFiles => service.directoryOut != null;
		} // class FileServiceSession

		#endregion

		private const string FileStampFormat = "yyyyMMddHHmmssffff";

		private DirectoryInfo directoryIn = null;
		private DirectoryInfo directoryOut = null;

		private string destinationId;
		private string[] fileNameFilter = null;
		private int priority;
		private int lastSessionId = 0;

		#region -- Ctor/Dtor/Config -------------------------------------------------------

		public DirectoryFileServiceItem(IServiceProvider sp, string name)
			: base(sp, name)
		{
		} // ctor

		protected override void OnBeginReadConfiguration(IDEConfigLoading config)
		{
			// check the id's
			if (String.IsNullOrEmpty(config.ConfigNew.GetAttribute("destination", String.Empty)))
				throw new ArgumentNullException("@destination id is missing.");

			// check the directories
			ValidateDirectory(config.ConfigNew, "in", true);
			ValidateDirectory(config.ConfigNew, "out", true);

			base.OnBeginReadConfiguration(config);
		} // OnBeginReadConfiguration

		protected override void OnEndReadConfiguration(IDEConfigLoading config)
		{
			base.OnEndReadConfiguration(config);

			// read the attributes
			var x = new XConfigNode(Server.Configuration[config.ConfigNew.Name], config.ConfigNew);

			this.destinationId = x.GetAttribute<string>("destination").ToUpper();
			this.priority = x.GetAttribute<int>("priority");

			this.fileNameFilter = x.GetAttribute<string>("inFilter").Split(new char[] { ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);

			// set directories
			this.directoryIn = x.GetAttribute<DirectoryInfo>("in");
			this.directoryOut = x.GetAttribute<DirectoryInfo>("out");
		} // proc OnEndReadConfiguration

		#endregion

		#region -- CreateFileService Session ----------------------------------------------

		private int GetSessionId()
			=> Interlocked.Increment(ref lastSessionId);

		IOdetteFileService IOdetteFileServiceFactory.CreateFileService(string destinationId, string password)
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

		#region -- Directory Helper -------------------------------------------------------

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
				if (ProcsDE.IsFilterEqual(fileDescription.VirtualFileName, cur))
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
				throw new ArgumentNullException("fileDescription");
			if (String.IsNullOrEmpty(fileDescription.VirtualFileName))
				throw new ArgumentNullException("VirtualFileName");
			if (fileDescription.FileStamp == null)
				throw new ArgumentNullException("FileStamp");

			return fileDescription.Originator + "#" +
				fileDescription.VirtualFileName + "#" +
				fileDescription.FileStamp.ToString(FileStampFormat, CultureInfo.InvariantCulture);
		} // func GetFileName

		private static IOdetteFile TrySplitFileName(string name)
		{
			if (String.IsNullOrEmpty(name))
				throw new ArgumentNullException("name");

			DateTime fileStamp;
			var m = Regex.Match(name, @"(\w+)#(\w+)#(\d{18})\.?.*");
			if (m.Success && DateTime.TryParseExact(m.Groups[3].Value, FileStampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out fileStamp))
			{
				return new OdetteFileMutable(m.Groups[2].Value, fileStamp, m.Groups[1].Value);
			}
			else
			{
				return null;
			}
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
				default:
					throw new ArgumentException("Invalid state.");
			}
		} // func GetInFileExtention

		/// <summary>Builds the file name with state extention.</summary>
		/// <param name="file"></param>
		/// <param name="state"></param>
		/// <returns></returns>
		internal FileInfo CreateInFileName(IOdetteFile file, OdetteInFileState state)
		{
			return new FileInfo(Path.Combine(directoryIn.FullName, GetFileName(file) + GetInFileExtention(state)));
		} // func CreateInFileName

		/// <summary>Builds the file name with state extention.</summary>
		/// <param name="file"></param>
		/// <param name="state"></param>
		/// <returns></returns>
		internal FileInfo CreateOutFileName(IOdetteFile file, OdetteOutFileState state)
		{
			return new FileInfo(Path.Combine(directoryOut.FullName, GetFileName(file) + GetOutFileExtention(state)));
		} // func CreateOutFileName

		private static void ChangeInFileState(FileInfo fileInfo, OdetteInFileState newState)
		{
			var fiNewFileName = Path.ChangeExtension(fileInfo.FullName, GetInFileExtention(newState));
			fileInfo.MoveTo(fiNewFileName);
		} // func ChangeInFileState

		private static void ChangeOutFileState(FileInfo fileInfo, OdetteOutFileState newState)
		{
			var fiNewFileName = Path.ChangeExtension(fileInfo.FullName, GetOutFileExtention(newState));
			fileInfo.MoveTo(fiNewFileName);
		} // func ChangeOutFileState

		#endregion
	} // class DirectoryFileServiceItem
}
