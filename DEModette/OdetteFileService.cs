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
using TecWare.DE.Server;
using TecWare.DE.Stuff;

namespace TecWare.DE.Odette
{
	#region -- interface IOdetteFileService2 ------------------------------------------

	/// <summary>Extends the file service session implementation for the server.</summary>
	public interface IOdetteFileService2 : IOdetteFileService
	{
		/// <summary>Sort order</summary>
		int Priority { get; }
	} // interface IOdetteFileService2

	#endregion

	#region -- interface IOdetteFileServiceFactory ------------------------------------

	/// <summary></summary>
	public interface IOdetteFileServiceFactory
	{
		/// <summary>Fits this fileservice to the given destination.</summary>
		/// <param name="destinationId">Destination, asked for.</param>
		/// <param name="password">Password of the destination.</param>
		/// <returns>File session or <c>null</c>, if the service is not responsible for the destination.</returns>
		/// <exception cref="OdetteFileServiceException">Will result in end session, with Service is currently not available.</exception>
		IOdetteFileService2 CreateFileService(string destinationId, string password);
	} // interface IOdetteFileServiceFactory

	#endregion

	#region -- class OdetteFileService ------------------------------------------------

	/// <summary>Implements a file service that host all collected file service
	/// sessions for the current destination.</summary>
	internal sealed class OdetteFileService : IOdetteFileService, IDisposable
	{
		private readonly string destinationId;
		private readonly IOdetteFileService[] services;

		#region -- Ctor/Dtor ----------------------------------------------------------

		public OdetteFileService(IServiceProvider sp, string destinationId, string password)
		{
			this.destinationId = destinationId;

			// Search for the file providers
			var item = sp.GetService<DEConfigItem>(false);
			if (item != null)
			{
				var collected = new List<IOdetteFileService2>();

				item.WalkChildren<IOdetteFileServiceFactory>(
					f =>
					{
						var t = f.CreateFileService(destinationId, password);
						if (t != null)
							collected.Add(t);
					},
					recursive: true
				);

				collected.Sort((a, b) =>
				{
					var r = String.Compare(a.DestinationId, b.DestinationId);
					return r == 0 ? a.Priority.CompareTo(b.Priority) : r;
				});

				services = collected.ToArray();
			}
		} // ctor

		public void Dispose()
		{
			if (services != null)
				Array.ForEach(services, s => s.Dispose());
		} // proc Dispose

		#endregion

		#region -- File service proxy implementation ----------------------------------

		/// <summary>Create/Overrides/Resumes a new odette file, to receive.</summary>
		/// <param name="file"></param>
		/// <param name="userData"></param>
		/// <returns></returns>
		public async Task<IOdetteFileWriter> CreateInFileAsync(IOdetteFile file, string userData)
		{
			foreach (var s in services)
			{
				var inFile = await s.CreateInFileAsync(file, userData);
				if (inFile != null)
					return inFile;
			}
			return null;
		} // func CreateInFile

		/// <summary>Get the files that can be committed.</summary>
		/// <returns></returns>
		public IEnumerable<IOdetteFileEndToEnd> GetEndToEnd()
		{
			foreach (var s in services)
				foreach (var i in s.GetEndToEnd())
					yield return i;
		} // func GetEndToEnd

		/// <summary>Files for send.</summary>
		/// <returns></returns>
		public IEnumerable<Func<IOdetteFileReader>> GetOutFiles()
		{
			foreach (var s in services)
				foreach (var i in s.GetOutFiles())
					yield return i;
		} // func GetOutFiles

		/// <summary>End to end received for a file.</summary>
		/// <param name="description"></param>
		public async Task<bool> UpdateOutFileStateAsync(IOdetteFileEndToEndDescription description)
		{
			foreach (var s in services)
			{
				if (await s.UpdateOutFileStateAsync(description))
					return true;
			}
			return false;
		} // proc UpdateOutFileState

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

		#endregion
	} // class OdetteFileService

	#endregion
}
