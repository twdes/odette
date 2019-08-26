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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using TecWare.DE.Data;

namespace TecWare.DE.Odette
{
	public sealed class Data //: IList, INotifyCollectionChanged
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
				=> stream.Dispose();

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

		public abstract class OdetteFile : ObservableObject, IOdetteFile, IEquatable<IOdetteFile>
		{
			private readonly string virtualFileName;
			private readonly DateTime fileStamp;
			private readonly string userData;

			protected OdetteFile(string virtualFileName, DateTime fileStamp, string userData)
			{
				this.virtualFileName = virtualFileName ?? throw new ArgumentNullException(nameof(virtualFileName));
				this.fileStamp = fileStamp;
				this.userData = userData ?? String.Empty;
			} // ctor


			public bool Equals(IOdetteFile other) 
				=> throw new NotImplementedException();

			public string VirtualFileName => virtualFileName;
			public DateTime FileStamp => fileStamp;
			public abstract string SourceOrDestination { get; }

			public string UserData => userData;
			public abstract string Status { get; }
		} // class OdetteFile

		#endregion

		#region -- class OdetteInFile -------------------------------------------------

		public sealed class OdetteInFile : OdetteFile, IOdetteFileEndToEnd
		{
			private readonly string sourceId;
			private OdetteInFileState state;
			
			public OdetteInFile(IOdetteFile file, string userData, string sourceId)
				: base(file.VirtualFileName, file.FileStamp, userData)
			{
				this.sourceId = sourceId;
				state = OdetteInFileState.Pending;
			} // ctor

			internal IOdetteFileWriter CreateWriter()
				=> null; // new OdetteFileWriter(this, );

			IOdetteFile IOdetteFileEndToEndDescription.Name => this;
			int IOdetteFileEndToEndDescription.ReasonCode => 0;
			string IOdetteFileEndToEndDescription.ReasonText => String.Empty;

			Task IOdetteFileEndToEnd.CommitAsync()
			{
				state = OdetteInFileState.Finished;
				throw new NotImplementedException();
			}
			internal Task CommitFileAsync(long recordCount, long unitCount)
			{
				// set ReasonCode
				state = OdetteInFileState.PendingEndToEnd; // skip receive
				throw new NotImplementedException();
			}

			public override string SourceOrDestination => sourceId;

			public OdetteInFileState InState => state;
			public override string Status => state.ToString();
		} // class OdetteInFileItem

		#endregion

		#region -- class OdetteOutFile ------------------------------------------------

		public sealed class OdetteOutFile : OdetteFile
		{
			private readonly string destinationId;
			private OdetteOutFileState state;

			public OdetteOutFile(IOdetteFile file, string userData, string destinationId)
				: base(file.VirtualFileName, file.FileStamp, userData)
			{
				this.destinationId = destinationId;
				state = OdetteOutFileState.Sent;
			} // ctor

			internal IOdetteFileReader CreateReader()
				=> null;

			internal Task SetTransmissionErrorAsync(OdetteAnswerReason answerReason, string reasonText, bool retryFlag)
			{
				state = OdetteOutFileState.Finished; // or OdetteOutFileState.Sent with retry;
				throw new NotImplementedException();
			}
			internal Task SetTransmissionStateAsync()
			{
				state = OdetteOutFileState.WaitEndToEnd;
				throw new NotImplementedException();
			}

			public Task SetEndToEndAsync(IOdetteFileEndToEndDescription endToEndDescription)
			{
				state = OdetteOutFileState.Finished; // OdetteOutFileState.ReceivedEndToEnd;
				throw new NotImplementedException();
			}

			public override string SourceOrDestination => destinationId;

			public OdetteOutFileState OutState => state;
			public override string Status => state.ToString();
		} // class OdetteOutFile

		#endregion

		#region -- class OdetteFileService --------------------------------------------

		public sealed class OdetteFileService : IOdetteFileService//, IList, INotifyCollectionChanged
		{
			private readonly List<OdetteFile> files = new List<OdetteFile>();

			public void Dispose()
			{
			} // proc Dispose

			private Task<IOdetteFileWriter> AddFileCore(OdetteFile file)
				=> throw new NotImplementedException();

			public void AddOutFile(string fileName, string userData)
			{
				//new OdetteOutFile(this.DestinationId)
			}

			Task<IOdetteFileWriter> IOdetteFileService.CreateInFileAsync(IOdetteFile file, string userData)
				=> AddFileCore(new OdetteInFile(file, userData, file.SourceOrDestination));

			IEnumerable<IOdetteFileEndToEnd> IOdetteFileService.GetEndToEnd()
				=> files.OfType<OdetteInFile>().Where(c => c.InState == OdetteInFileState.PendingEndToEnd);

			IEnumerable<Func<IOdetteFileReader>> IOdetteFileService.GetOutFiles()
				=> files.OfType<OdetteOutFile>().Where(c => c.OutState == OdetteOutFileState.Sent).Select(c => new Func<IOdetteFileReader>(c.CreateReader));
			
			async Task<bool> IOdetteFileService.UpdateOutFileStateAsync(IOdetteFileEndToEndDescription description)
			{
				var outFile = files.OfType<OdetteOutFile>().FirstOrDefault(c => c.OutState == OdetteOutFileState.WaitEndToEnd && c.Equals(description.Name));
				if (outFile != null)
				{
					await outFile.SetEndToEndAsync(description);
					return true;
				}

				return false;
			} // func IOdetteFileService.UpdateOutFileStateAsync

			bool IOdetteFileService.SupportsInFiles => true;
			bool IOdetteFileService.SupportsOutFiles => true;

			/// <summary>Other endpoint of the communication.</summary>
			public string DestinationId => throw new NotImplementedException();

			public string Password => throw new NotImplementedException();
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
				if (e != null)
					Debug.Print(e.ToString());
				else
					Debug.Print(message);
			} // proc LogExcept

			protected override void LogInfo(string message) 
				=> Debug.Print(message);

			/// <summary>Own id</summary>
			protected override string OdetteId => throw new NotImplementedException();
			/// <summary>Own password</summary>
			protected override string OdettePassword => throw new NotImplementedException();
		} // class OdetteFtp

		#endregion

		private readonly List<OdetteFileService> destinations = new List<OdetteFileService>();

		public Data()
		{
		}

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
			=> destinations.FirstOrDefault(c => c.DestinationId == initiatorCode && (c.Password == null || c.Password == password));
	} // class Data
}
