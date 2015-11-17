using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TecWare.DE.Server;
using TecWare.DE.Stuff;

namespace TecWare.DE.Odette
{
	#region -- enum OdetteFileFormat ----------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public enum OdetteFileFormat
	{
		Fixed,
		Variable,
		Unstructured,
		Text
	} // enum OdetteFileFormat

	#endregion

	#region -- enum OdetteOutFileState --------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public enum OdetteOutFileState
	{
		/// <summary>File needs to send.</summary>
		New,
		/// <summary>Waiting for the End to End response.</summary>
		WaitEndToEnd,
		/// <summary>Communication finished.</summary>
		Finished
	} // enum OdetteOutFileState

	#endregion

	#region -- enum OdetteOutFileState --------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public enum OdetteInFileState
	{
		/// <summary>New incoming file.</summary>
		Pending,
		/// <summary>File is complete received.</summary>
		Received,
		/// <summary>File is successfully processed.</summary>
		PendingEndToEnd,
		/// <summary>EndToEnd sent successful.</summary>
		Finished
	} // enum OdetteInFileState

	#endregion

	#region -- interface IOdetteFile ----------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary>Basic file description</summary>
	public interface IOdetteFile
	{
		/// <summary>File name</summary>
		string VirtualFileName { get; }
		/// <summary>Time stamp of the file</summary>
		DateTime FileStamp { get; }
		/// <summary>Source of the file.</summary>
		string Originator { get; }
	} // interface IOdetteFile

	#endregion

	#region -- interface IOdetteFileDescription -----------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary>Description of the file format.</summary>
	public interface IOdetteFileDescription : IOdetteFile
	{
		/// <summary>Format of the file.</summary>
		OdetteFileFormat Format { get; }
		/// <summary>If the format is fixed, this value holds the record size.</summary>
		int MaximumRecordSize { get; }
		/// <summary>Estimated file size  (in 1kb).</summary>
		long FileSize { get; }
		/// <summary>Estimated file size (unpacked, in 1kb).</summary>
		long FileSizeUnpacked { get; }
		/// <summary>Description</summary>
		string Description { get; }
	} // interface IOdetteFileDescription

	#endregion

	#region -- interface IOdetteFileWriter ----------------------------------------------

	public interface IOdetteFileWriter : IDisposable
	{
		/// <summary>Writes data to the target file.</summary>
		/// <param name="buf"></param>
		/// <param name="offset"></param>
		/// <param name="count"></param>
		/// <param name="isEof"></param>
		void Write(byte[] buf, int offset, int count, bool isEof);
		/// <summary>File is received successful.</summary>
		/// <returns></returns>
		void CommitFile(long recordCount, long unitCount);

		//void SetEndToEnd();
		IOdetteFile Name { get; }
	} // interface IOdetteInFile 

	#endregion

	#region -- interface IOdetteFileReader ----------------------------------------------

	public interface IOdetteFileReader : IDisposable
	{
		/// <summary></summary>
		/// <param name="buf"></param>
		/// <param name="offset"></param>
		/// <param name="count"></param>
		int Read(byte[] buf, int offset, int count);

		//void SetEndToEnd();
		IOdetteFile Name { get; }
	} // interface IOdetteFileReader 

	#endregion

	#region -- interface IOdetteFilePosition --------------------------------------------

	public interface IOdetteFilePosition
	{
		/// <summary>Reposition for the file (1k steps or records).</summary>
		/// <param name="position"></param>
		long Seek(long position);

		/// <summary>Position in 1k or records</summary>
		long CurrentPosition { get; }
	} // interface IOdetteFilePosition

	#endregion

	#region -- interface IOdetteEndToEnd ------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public interface IOdetteFileEndToEnd
	{
		/// <summary>Marks the end to message as sent.</summary>
		void Commit();

		/// <summary>Dateiname</summary>
		IOdetteFile Name { get; }

		/// <summary>Reason code, 0 for a positive end to end, != zero for a negative</summary>
		int ReasonCode { get; }
		/// <summary>Text of the negative end to end.</summary>
		string ReasonText { get; }
		/// <summary>8 byte user data.</summary>
		string UserData { get; }
	} // interface IOdetteFileEndToEnd

	#endregion

	//#region -- interface IOdetteOutFile -------------------------------------------------

	//public interface IOdetteOutFile : IOdetteFile
	//{
	//	void SetTransmissionError(OdetteAnswerReason answerReason, object reasonText, bool retryFlag);

	//	Stream GetDataStream();

	//	void SetTransmissionState();

	//	//OdetteFileFormat Format { get; }
	//	//
	//	//
	//	//long FileSizeOriginal { get; }
	//	//string Description { get; }
	//} // interface IOdetteOutFile 

	//#endregion

	#region -- interface IOdetteFileService ---------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public interface IOdetteFileService : IDisposable
	{
		/// <summary>Creates a new file in the service.</summary>
		/// <param name="fileDescription">Description of the new file.</param>
		/// <returns></returns>
		IOdetteFileWriter CreateInFile(IOdetteFileDescription fileDescription, string userData);
		/// <summary>List of end to end messages, that need to send.</summary>
		/// <returns></returns>
		IEnumerable<IOdetteFileEndToEnd> GetEndToEnd();
		
		/// <summary>Id of the destination.</summary>
		string DestinationId { get; }
		/// <summary>Sort order</summary>
		int Priority { get; }

		/// <summary>Can the service receive files.</summary>
		bool SupportsInFiles { get; }
		/// <summary>Can the service send files.</summary>
		bool SupportsOutFiles { get; }
	} // interface IOdetteFileService

	#endregion

	#region -- interface IOdetteFileServiceFactory --------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public interface IOdetteFileServiceFactory
	{
		/// <summary>Fits this fileservice to the given destination.</summary>
		/// <param name="destinationId">Destination, asked for.</param>
		/// <param name="password">Password of the destination.</param>
		/// <returns></returns>
		IOdetteFileService CreateFileService(string destinationId, string password);
	} // interface IOdetteFileServiceFactory

	#endregion

	#region -- class OdetteFileService --------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	internal sealed class OdetteFileService : IDisposable
	{
		private readonly IServiceProvider sp;
		private readonly string destinationId;
		private readonly IOdetteFileService[] services;

		#region -- Ctor/Dtor --------------------------------------------------------------

		public OdetteFileService(IServiceProvider sp, string destinationId, string password)
		{
			this.sp = sp;
			this.destinationId = destinationId;

			// Search for the file providers
			var item = sp.GetService<DEConfigItem>(false);
			if (item != null)
			{
				var collected = new List<IOdetteFileService>();

				item.WalkChildren<IOdetteFileServiceFactory>(
					f =>
					{
						var t = f.CreateFileService(destinationId, password);
						if (t != null)
							collected.Add(t);
					},
					recursive: true
				);

				services = collected.ToArray();
			}
		} // ctor

		public void Dispose()
		{
			if (services != null)
				Array.ForEach(services, s => s.Dispose());
		} // proc Dispose

		#endregion

		/// <summary>Create/Overrides/Resumes a new odette file, to receive.</summary>
		/// <param name="fileDescription"></param>
		/// <param name="userData"></param>
		/// <returns></returns>
		public IOdetteFileWriter CreateInFile(IOdetteFileDescription fileDescription, string userData)
		{
			foreach (var s in services)
			{
				var inFile = s.CreateInFile(fileDescription, userData);
				if (inFile != null)
					return inFile;
			}
			return null;
		} // func CreateInFile

		public IEnumerable<IOdetteFileEndToEnd> GetEndToEnd()
		{
			foreach (var s in services)
				foreach (var i in s.GetEndToEnd())
					yield return i;
		} // func GetEndToEnd

		/// <summary>File service destination id</summary>
		public string DestinationId => destinationId;

		/// <summary>Can one file service create a file.</summary>
		public bool SupportsInFiles
		{
			get
			{
				var r = false;
				for (var i = 0; i < services.Length; i++)
					r |= services[i].SupportsInFiles;
				return r;
			}
		} // prop SupportsInFiles

		/// <summary>Can one file service send a file.</summary>
		public bool SupportsOutFiles
		{
			get
			{
				var r = false;
				for (var i = 0; i < services.Length; i++)
					r |= services[i].SupportsOutFiles;
				return r;
			}
		} // prop SupportsInFiles
	} // class OdetteFileService

	#endregion
}
