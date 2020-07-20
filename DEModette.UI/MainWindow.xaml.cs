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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TecWare.DE.Stuff;

namespace TecWare.DE.Odette
{
	public partial class MainWindow : Window
	{
		private readonly Data data;

		public MainWindow()
		{
			InitializeComponent();

			data = new Data(GetProfileName());
			DataContext = data;
		} // ctor

		private static string GetProfileName()
		{
			var args = Environment.GetCommandLineArgs();
			for (var i = 0; i < args.Length; i++)
			{
				if (args[i] == "-p" && i < args.Length - 1)
					return args[i + 1];

			}
			return null;
		} // func GetProfileName

		private async Task RunAsync(object sender, Func<Task> task)
		{
			var ctrl = sender as Control;
			if (ctrl != null)
				ctrl.IsEnabled = false;

			try
			{
				await task();
			}
			catch(Exception e)
			{
				MessageBox.Show(e.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
			finally
			{
				if (ctrl != null)
					ctrl.IsEnabled = true;
			}
		} // proc RunAsync

		private void PropertiesChangeClick(object sender, RoutedEventArgs e)
		{
			if (new UI.OdetteParameterDialog().ShowEdit(this, data))
				RunAsync(sender, () => data.SaveAsync()).Silent();
		} // event PropertiesChangeClick

		private void NewDestinationClicked(object sender, RoutedEventArgs e)
		{
			//var newDest = new UI.ProposedObject()
			//new UI.OdetteFileServiceDialog().ShowNew(this, )

			var dlg = new UI.OdetteFileServiceDialog { Owner = this };
			dlg.removeButton.Visibility = Visibility.Collapsed;

			dlg.nameText.Text = "new";

			if (dlg.ShowDialog() == true)
			{
				var dest = data.AddDestination(dlg.nameText.Text);
				dest.DestinationId = dlg.odetteIdText.Text;
				dest.Password = dlg.odettePasswordText.Text;
				dest.DestinationHost = dlg.destinationHostText.Text;
				dest.DestinationPort = Int32.Parse(dlg.destinationPortText.Text);
				dest.UseSsl = dlg.destinationUseSsl.IsChecked.Value;

				if (data.IsDirty)
					RunAsync(sender, () => data.SaveAsync()).Silent();
			}
		}

		private void ChangeDestinationClicked(object sender, RoutedEventArgs e)
		{
			var dest = (IOdetteDestination)((Button)sender).DataContext;
			var dlg = new UI.OdetteFileServiceDialog { Owner = this };
			dlg.nameText.Text = dest.Name;
			dlg.nameText.IsReadOnly = true;
			dlg.odetteIdText.Text = dest.DestinationId;
			dlg.odettePasswordText.Text = dest.Password;
			dlg.destinationHostText.Text = dest.DestinationHost;
			dlg.destinationPortText.Text = dest.DestinationPort.ToString();
			dlg.destinationUseSsl.IsChecked = dest.UseSsl;

			if (dlg.ShowDialog() == true)
			{
				if (dlg.removeMe)
					dest.Remove();
				else
				{
					dest.DestinationId = dlg.odetteIdText.Text;
					dest.Password = dlg.odettePasswordText.Text;
					dest.DestinationHost = dlg.destinationHostText.Text;
					dest.DestinationPort = Int32.Parse(dlg.destinationPortText.Text);
					dest.UseSsl = dlg.destinationUseSsl.IsChecked.Value;
				}

				if (data.IsDirty)
					RunAsync(sender, () => data.SaveAsync()).Silent();
			}
		}

		private async Task AddFilesAsync(IOdetteDestination dest, string[] fileNames)
		{
			foreach (var f in fileNames)
				await dest.AddOutFileAsync(f, null);

			await dest.RunAsync();
		} // proc func AddFilesAsync

		private void SendFileClicked(object sender, RoutedEventArgs e)
		{
			var dest = (IOdetteDestination)((Button)sender).CommandParameter;

			var dlg = new OpenFileDialog
			{
				Filter = "All Files (*.*)|*.*",
				CheckFileExists = true,
				Multiselect = true
			};
			if (dlg.ShowDialog(this) == true)
				AddFilesAsync(dest, dlg.FileNames).Silent(RunDestinationFailed);
		}

		private void StartDestinationClicked(object sender, RoutedEventArgs e)
		{
			var dest = (IOdetteDestination)((Button)sender).DataContext;
			dest.RunAsync().Silent(RunDestinationFailed);
		}

		private void RunDestinationFailed(Exception e)
			=> MessageBox.Show(e.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);

		private void ListenClick(object sender, RoutedEventArgs e)
		{
			if (data.IsRunning)
				data.Stop();
			else
				data.Start();
		}
	} // class MainWindow
}
