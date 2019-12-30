using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace TecWare.DE.Odette.UI
{
	public partial class OdetteFileServiceDialog : Window
	{
		public bool removeMe = false;

		public OdetteFileServiceDialog()
		{
			InitializeComponent();
		}

		private void OkClicked(object sender, RoutedEventArgs e)
		{
			if(String.IsNullOrEmpty(nameText.Text)
				|| !Int32.TryParse(destinationPortText.Text, out var _))
			{
				MessageBox.Show("Input wrong.");
				return;
			}

			DialogResult = true;
		}

		private void RemoveClicked(object sender, RoutedEventArgs e)
		{
			removeMe = true;
			DialogResult = true;
		}

		private void CancelClicked(object sender, RoutedEventArgs e)
		{
			DialogResult = false;
		}
	}
}
