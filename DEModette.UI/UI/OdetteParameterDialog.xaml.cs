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
	public partial class OdetteParameterDialog : Window
	{
		public OdetteParameterDialog()
		{
			InitializeComponent();
		}

		public bool ShowEdit(Window owner, Data data)
		{
			Owner = owner;

			var proposedData = new ProposedObject(data);
			DataContext = proposedData;
			if(ShowDialog() == true)
			{
				if (proposedData.IsDirty)
				{
					proposedData.Update();
					return true;
				}
			}
			return false;
		} // proc ShowEdit

		private void OkClicked(object sender, RoutedEventArgs e)
		{
			if (Int32.TryParse(listenPortText.Text, out var _))
				DialogResult = true;
			else
				MessageBox.Show("Port is not a number!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
		}

		private void CancelClicked(object sender, RoutedEventArgs e)
		{
			DialogResult = false;
		}
	}
}
