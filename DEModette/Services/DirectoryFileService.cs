using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

			public FileItem(FileInfo fileInfo, IOdetteFileDescription fileDescription)
			{
				this.fileInfo = fileInfo;

				this.virtualFileName = fileDescription.VirtualFileName;
				this.fileStamp = fileDescription.FileStamp;
				this.originator = fileDescription.Originator;

				this.format = fileDescription.Format;
				this.maximumRecordSize = fileDescription.MaximumRecordSize;
				this.fileSize = fileDescription.FileSize;
				this.fileSizeUnpacked = fileDescription.FileSizeUnpacked;
				this.description = fileDescription.Description;

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

				xExtentions.Save(GetExtendedFile());
			} // ctor

			public FileItem(FileInfo fileInfo, string virtualFileName, DateTime fileStamp, string originator)
			{
				this.fileInfo = fileInfo;
				this.virtualFileName = virtualFileName;
				this.fileStamp = fileStamp;
				this.originator = originator;

				// read the extented informations
				var fi = new FileInfo(GetExtendedFile());
				if (fi.Exists)
				{
					xExtentions = XDocument.Load(fi.FullName);
					var xDescription = xExtentions.Root.Element("description") ?? new XElement("description");

					format = xDescription.GetAttribute("format", OdetteFileFormat.Unstructured);
					maximumRecordSize = xDescription.GetAttribute("maximumRecordSize", 0);
					fileSize = xDescription.GetAttribute("fileSize", -1L);
					if (fileSize < 0)
						fileSize = fileInfo.Length / 1024;
					fileSizeUnpacked = xDescription.GetAttribute("fileSizeUnpacked", fileSize);
					description = xDescription.Value ?? String.Empty;
				}
				else
				{
					xExtentions = new XDocument();
					format = OdetteFileFormat.Unstructured;
					maximumRecordSize = 0;
					fileSize = fileInfo.Length / 1024;
					fileSizeUnpacked = fileSize;
					description = null;
				}
			} // ctor

			private string GetExtendedFile()
				=> Path.ChangeExtension(fileInfo.FullName, ".xml");

			public IOdetteFileWriter OpenWrite()
				=> new OdetteFileStream(this, false);

			public IOdetteFileReader OpenRead()
				=> new OdetteFileStream(this, true);

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
				xCommit = item.Extensions.Root.Element("commit") ?? new XElement("commit") ;
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

		private sealed class OdetteFileStream : IOdetteFileWriter, IOdetteFileReader, IOdetteFilePosition, IDisposable
		{
			private readonly FileItem item;
			private FileStream stream = null;

			public OdetteFileStream(FileItem item, bool readOnly)
			{
				this.item = item;

				stream = readOnly ?
					item.FileInfo.OpenRead() :
					item.FileInfo.OpenWrite();

				if (!readOnly)
					Seek(stream.Length); // move to the end, for restart
			} // ctor

			public void Dispose()
			{
				Procs.FreeAndNil(ref stream);
			} // proc Dispose

			public void Write(byte[] buf, int offset, int count, bool isEof)
			{
				if (stream == null)
					throw new ArgumentNullException("stream");

				stream.Write(buf, offset, count);
			} // proc Write

			public int Read(byte[] buf, int offset, int count)
			{
				if (stream == null)
					throw new ArgumentNullException("stream");

				return stream.Read(buf, offset, count);
			} // func Read

			public void CommitFile(long recordCount, long unitCount)
			{
				// validate file size
				switch (item.Format)
				{
					case OdetteFileFormat.Fixed:
						if (recordCount != unitCount / item.MaximumRecordSize)
							throw new OdetteFileServiceException(OdetteAnswerReason.InvalidRecordCount);
						goto case OdetteFileFormat.Unstructured;
					case OdetteFileFormat.Text:
					case OdetteFileFormat.Unstructured:
						if (stream.Length != unitCount)
							throw new OdetteFileServiceException(OdetteAnswerReason.InvalidByteCount);
						break;
					case OdetteFileFormat.Variable:
						// count records -> in xml
						break;
				}

				// close the stream
				Procs.FreeAndNil(ref stream);

				// rename file to show that it is received
				ChangeInFileState(item.FileInfo, OdetteInFileState.Received);
			} // proc CommitFile

			public long Seek(long position)
			{
				switch (item.Format)
				{
					case OdetteFileFormat.Unstructured:
					case OdetteFileFormat.Text:
						{
							var newpos = position >> 10;
							if (newpos > stream.Length)
								newpos = stream.Length & ~0x3FF;
							return stream.Seek(newpos, SeekOrigin.Begin) >> 10;
						}

					case OdetteFileFormat.Fixed:
						{
							var newpos = position * item.MaximumRecordSize;
							if (newpos > stream.Length)
								newpos = stream.Length / item.MaximumRecordSize * item.MaximumRecordSize;
							return stream.Seek(newpos, SeekOrigin.Begin) / item.MaximumRecordSize;
						}

					case OdetteFileFormat.Variable: // no restart allowed
						stream.Position = 0;
						return 0;

					default:
						throw new ArgumentException("unknown file format.");
				}
			} // proc Seek

			public long CurrentPosition
			{
				get
				{
					switch (item.Format)
					{
						case OdetteFileFormat.Unstructured:
						case OdetteFileFormat.Text:
							return stream.Position & ~0x3FF;

						case OdetteFileFormat.Fixed:
							return stream.Position / item.MaximumRecordSize;

						case OdetteFileFormat.Variable: // no restart allowed
							return 0;

						default:
							throw new ArgumentException("unknown file format.");
					}
				}
			} // prop CurrentPosition

			public IOdetteFile Name => item;
		} // class OdetteFileStream

		#endregion

		#region -- class FileServiceSession -----------------------------------------------

		///////////////////////////////////////////////////////////////////////////////
		/// <summary></summary>
		private sealed class FileServiceSession : IOdetteFileService
		{
			private readonly DirectoryFileServiceItem service;

			public FileServiceSession(DirectoryFileServiceItem service)
			{
				this.service = service;
			} // ctor

			public void Dispose()
			{
			} // proc Dispose

			public IOdetteFileWriter CreateInFile(IOdetteFileDescription fileDescription, string userData)
			{
				if (!service.IsInFileAllowed(fileDescription))
					return null;

				var fi = service.CreateFileName(fileDescription, OdetteInFileState.Pending);

				// check if the file exists
				if (File.Exists(Path.ChangeExtension(fi.FullName, GetInFileExtention(OdetteInFileState.Received))) ||
					File.Exists(Path.ChangeExtension(fi.FullName, GetInFileExtention(OdetteInFileState.PendingEndToEnd))) ||
					File.Exists(Path.ChangeExtension(fi.FullName, GetInFileExtention(OdetteInFileState.Finished))))
					throw new OdetteFileServiceException(OdetteAnswerReason.DuplicateFile, "File already exists.", false);

				// open the file to write
				var fileItem = new FileItem(fi, fileDescription);
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
					string virtualFileName;
					DateTime fileStamp;
					string originator;
					if (TrySplitFileName(fi.Name, out originator, out virtualFileName, out fileStamp))
						yield return new FileEndToEnd(new FileItem(fi, virtualFileName, fileStamp, originator));
				}
			} // func GetEndToEnd

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

				return new FileServiceSession(this);
			}
			else
				return null;
		} // func CreateFileService

		#endregion

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

		private static bool TrySplitFileName(string name, out string originator, out string virtualFileName, out DateTime fileStamp)
		{
			if (String.IsNullOrEmpty(name))
				throw new ArgumentNullException("name");

			var m = Regex.Match(name, @"(\w+)#(\w+)#(\d{18})\.?.*");
			if (m.Success &&
				DateTime.TryParseExact(m.Groups[3].Value, FileStampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out fileStamp))
			{
				originator = m.Groups[1].Value;
				virtualFileName = m.Groups[2].Value;
				return true;
			}
			else
			{
				originator = null;
				virtualFileName = null;
				fileStamp = DateTime.MinValue;
				return false;
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
					return ".done";
				case OdetteInFileState.Finished:
					return ".arc";
				default:
					throw new ArgumentException("Invalid state.");
			}
		} // func GetInFileExtention

		/// <summary>Builds the file name with state extention.</summary>
		/// <param name="fileDescription"></param>
		/// <param name="state"></param>
		/// <returns></returns>
		internal FileInfo CreateFileName(IOdetteFile fileDescription, OdetteInFileState state)
		{
			return new FileInfo(Path.Combine(directoryIn.FullName, GetFileName(fileDescription) + GetInFileExtention(state)));
    } // func CreateFileName

		private static void ChangeInFileState(FileInfo fileInfo, OdetteInFileState newState)
		{
			var fiNewFileName = Path.ChangeExtension(fileInfo.FullName, GetInFileExtention(newState));
			fileInfo.MoveTo(fiNewFileName);
		} // func ChangeInFileState
	} // class DirectoryFileServiceItem
}
