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
using System.Threading.Tasks;

namespace TecWare.DE.Odette
{
	#region -- enum OdetteFileFormat --------------------------------------------------

	/// <summary></summary>
	public enum OdetteFileFormat
	{
		/// <summary>Record file with a fixed record size.</summary>
		Fixed,
		/// <summary>Record file with a variable record size.</summary>
		Variable,
		/// <summary>Binary file.</summary>
		Unstructured,
		/// <summary>Text file (is the responsiblilty of the file server to handle the charsets).</summary>
		Text
	} // enum OdetteFileFormat

	#endregion

	#region -- enum OdetteOutFileState ------------------------------------------------

	/// <summary></summary>
	public enum OdetteOutFileState
	{
		/// <summary>Not used.</summary>
		New,
		/// <summary>File needs to send.</summary>
		Sent,
		/// <summary>Waiting for the End to End response.</summary>
		WaitEndToEnd,
		/// <summary>Communication finished.</summary>
		ReceivedEndToEnd,
		/// <summary>Not used.</summary>
		Finished
	} // enum OdetteOutFileState

	#endregion

	#region -- enum OdetteInFileState -------------------------------------------------

	/// <summary></summary>
	public enum OdetteInFileState
	{
		/// <summary>New incoming file.</summary>
		Pending,
		/// <summary>File is received full, but not processed.</summary>
		Received,
		/// <summary>File is successfully processed.</summary>
		PendingEndToEnd,
		/// <summary>EndToEnd sent successful.</summary>
		Finished
	} // enum OdetteInFileState

	#endregion

	#region -- interface IOdetteFile --------------------------------------------------

	/// <summary>Basic file description</summary>
	public interface IOdetteFile
	{
		/// <summary>File name</summary>
		string VirtualFileName { get; }
		/// <summary>Time stamp of the file</summary>
		DateTime FileStamp { get; }
		/// <summary>Source or destination of the file.</summary>
		string SourceOrDestination { get; }
	} // interface IOdetteFile

	#endregion

	#region -- class OdetteFileImmutable ----------------------------------------------

	/// <summary>Implementation of odette file.</summary>
	public sealed class OdetteFileImmutable : IOdetteFile
	{
		private readonly string virtualFileName;
		private readonly DateTime fileStamp;
		private readonly string sourceOrDestination;

		/// <summary></summary>
		/// <param name="virtualFileName"></param>
		/// <param name="fileStamp"></param>
		/// <param name="sourceOrDestination"></param>
		public OdetteFileImmutable(string virtualFileName, DateTime fileStamp, string sourceOrDestination)
		{
			this.virtualFileName = virtualFileName;
			this.fileStamp = fileStamp;
			this.sourceOrDestination = sourceOrDestination;
		} // ctor

		/// <summary></summary>
		/// <returns></returns>
		public override string ToString()
			=> FormatFileName(this, "");

		/// <summary></summary>
		public string VirtualFileName => virtualFileName;
		/// <summary></summary>
		public DateTime FileStamp => fileStamp;
		/// <summary></summary>
		public string SourceOrDestination => sourceOrDestination;

		/// <summary></summary>
		/// <param name="file"></param>
		/// <param name="userData"></param>
		/// <returns></returns>
		public static string FormatFileName(IOdetteFile file, string userData)
			=> file.SourceOrDestination + "/" + file.VirtualFileName + "[userData=" + userData + "]";
	} // class OdetteFileImmutable

	#endregion

	#region -- interface IOdetteFileDescription ---------------------------------------

	/// <summary>Description of the file format (optional).</summary>
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

	#region -- interface IOdetteFileWriter --------------------------------------------

	/// <summary></summary>
	public interface IOdetteFileWriter : IDisposable
	{
		/// <summary>Writes data to the target file.</summary>
		/// <param name="buf"></param>
		/// <param name="offset"></param>
		/// <param name="count"></param>
		/// <param name="isEoR">marks a end of the current record or the complete file.</param>
		Task WriteAsync(byte[] buf, int offset, int count, bool isEoR);
		/// <summary>File is received successful.</summary>
		/// <returns></returns>
		Task CommitFileAsync(long recordCount, long unitCount);

		/// <summary>File name. The description is not used.</summary>
		IOdetteFile Name { get; }

		/// <summary>Total length in bytes.</summary>
		long TotalLength { get; }
		/// <summary>Number of records, only for fixed or variable files. Should be zero in other cases.</summary>
		long RecordCount { get; }
	} // interface IOdetteInFile 

	#endregion

	#region -- interface IOdetteFileReader --------------------------------------------

	/// <summary>Reads a file from the file service.</summary>
	public interface IOdetteFileReader : IDisposable
	{
		/// <summary>Read a chunk of bytes</summary>
		/// <param name="buf">Buffer to fill</param>
		/// <param name="offset"></param>
		/// <param name="count"></param>
		/// <returns>Bytes copied in the buffer. <c>isEoR</c> marks a end of the current record or the complete file.</returns>
		Task<(int readed, bool isEoR)> ReadAsync(byte[] buf, int offset, int count);

		/// <summary>Transmission failed.</summary>
		/// <param name="answerReason"></param>
		/// <param name="reasonText"></param>
		/// <param name="retryFlag"></param>
		Task SetTransmissionErrorAsync(OdetteAnswerReason answerReason, string reasonText, bool retryFlag);
		/// <summary>File transmitted successful.</summary>
		Task SetTransmissionStateAsync();

		/// <summary>Description or name of the file. Is the description missing, it will be sent as binary file.</summary>
		IOdetteFile Name { get; }
		/// <summary>UserData for the send operation.</summary>
		string UserData { get; }

		/// <summary>Total length in bytes.</summary>
		long TotalLength { get; }
		/// <summary>Number of records, only for fixed or variable files. Should be zero in other cases.</summary>
		long RecordCount { get; }
	} // interface IOdetteFileReader 

	#endregion

	#region -- interface IOdetteFilePosition ------------------------------------------

	/// <summary>Extention for the IOdetteFileReader or IOdetteFileWrite to change
	/// the current read or write position.</summary>
	public interface IOdetteFilePosition
	{
		/// <summary>Reposition for the file (1k steps or records).</summary>
		/// <param name="position"></param>
		Task<long> SeekAsync(long position);
	} // interface IOdetteFilePosition

	#endregion

	#region -- interface IOdetteFileEndToEndDescription -------------------------------

	/// <summary>Description of a end to end message.</summary>
	public interface IOdetteFileEndToEndDescription
	{
		/// <summary>Dateiname</summary>
		IOdetteFile Name { get; }

		/// <summary>Reason code, 0 for a positive end to end, != zero for a negative</summary>
		int ReasonCode { get; }
		/// <summary>Text of the negative end to end.</summary>
		string ReasonText { get; }
		/// <summary>8 byte user data.</summary>
		string UserData { get; }
	} // interface IOdetteFileEndToEndDescription

	#endregion

	#region -- interface IOdetteFileEndToEnd ------------------------------------------

	/// <summary>Implements a end to end return of the file service.</summary>
	public interface IOdetteFileEndToEnd : IOdetteFileEndToEndDescription
	{
		/// <summary>Marks the end to message as sent.</summary>
		Task CommitAsync();
	} // interface IOdetteFileEndToEnd

	#endregion

	#region -- interface IOdetteFileService -------------------------------------------

	/// <summary>Implements a file service session for oftp.</summary>
	public interface IOdetteFileService : IDisposable
	{
		/// <summary>Creates a new file in the service.</summary>
		/// <param name="file">Description or the file name. If the description is missing, the file will be handled as a normal binary file.</param>
		/// <param name="userData"></param>
		/// <returns></returns>
		Task<IOdetteFileWriter> CreateInFileAsync(IOdetteFile file, string userData);
		/// <summary>List of end to end messages, that need to send.</summary>
		/// <returns></returns>
		IEnumerable<IOdetteFileEndToEnd> GetEndToEnd();

		/// <summary>Files for send.</summary>
		/// <returns></returns>
		IEnumerable<Func<IOdetteFileReader>> GetOutFiles();

		/// <summary>End to end received for a file.</summary>
		/// <param name="description"></param>
		Task<bool> UpdateOutFileStateAsync(IOdetteFileEndToEndDescription description);

		/// <summary>Id of the destination.</summary>
		string DestinationId { get; }

		/// <summary>Can the service receive files.</summary>
		bool SupportsInFiles { get; }
		/// <summary>Can the service send files.</summary>
		bool SupportsOutFiles { get; }
	} // interface IOdetteFileService

	#endregion
}
