using System;
using System.IO;
using System.Threading.Tasks;

namespace TecWare.DE.Odette.Network
{
	#region -- class OdetteNetworkStream ------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	internal sealed class OdetteNetworkStream : IOdetteFtpChannel, IDisposable
	{
		private const byte Version = 1;
		private const byte Flags = 0;
		private const byte StreamTransmissionHeader = (Version << 4) | (Flags & 4);
		
		private readonly Stream stream;
		private readonly string userData;

		#region -- Ctor/Dtor --------------------------------------------------------------

		public OdetteNetworkStream(Stream stream, string userData)
		{
			this.stream = stream;
			this.userData = userData;
		} // ctor

		public void Dispose()
		{
			stream.Dispose();
		} // proc Dispose
		
		public Task DisconnectAsync()
		{
			return Task.Factory.StartNew(stream.Close);
		} // func DisconnectAsync

		#endregion

		#region -- Receive ----------------------------------------------------------------

		private async Task ReadAsync(byte[] buffer, int length)
		{
			var ofs = 0;
			do
			{
				var r = await stream.ReadAsync(buffer, ofs, length - ofs);
				ofs += r;
				if (r <= 0)
					throw new Exception("eof"); // todo:
			} while (ofs < length);
		} // func ReadAsync

		public async Task<int> ReceiveAsync(byte[] buffer)
		{
			var header = new byte[4];

			await ReadAsync(header, 4);

			// check signature
			if (header[0] != StreamTransmissionHeader)
				throw new Exception("protocol error"); // todo:

			// read length of buffer
			var length = ((int)header[1] << 16 | (int)header[2] << 8 | (int)header[3]) - 4;
			if (length < 1 || length > buffer.Length)
				throw new Exception("protocol error"); // todo: 

			// read data
			await ReadAsync(buffer, length);
			return length;
		} // func ReceiveAsync

		#endregion

		#region -- Send -------------------------------------------------------------------

		public async Task SendAsync(byte[] buffer, int filled)
		{
			var header = new byte[4];
			var tmp = filled + 4;
			header[0] = StreamTransmissionHeader;
			header[1] = unchecked((byte)(tmp >> 16));
			header[2] = unchecked((byte)(tmp >> 8));
			header[3] = unchecked((byte)tmp);
			await stream.WriteAsync(header, 0, 4);
			await stream.WriteAsync(buffer, 0, filled);
		} // proc SendAsync

		#endregion

		public string UserData => userData;
	} // class OdetteNetworkStream

	#endregion
}
