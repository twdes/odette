using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Reflection;

namespace TecWare.DE.Odette.UI
{
	public class ProposedObject : DynamicObject, INotifyPropertyChanged
	{
		private const string changedSuffix = "Changed";

		#region -- class Property -----------------------------------------------------------

		private sealed class Property
		{
			private readonly ProposedObject owner;
			private readonly PropertyInfo propertyInfo;

			private bool isChanged = false;
			private object value = null;

			public Property(ProposedObject owner, PropertyInfo propertyInfo)
			{
				this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
				this.propertyInfo = propertyInfo ?? throw new ArgumentNullException(nameof(propertyInfo));
			} // ctor

			private void SetDirty()
			{
				if (!isChanged)
				{
					isChanged = true;
					owner.OnPropertyChanged(propertyInfo.Name + changedSuffix);
					owner.SetDirty();
				}
			} // proc SetDirty

			private void SetProposedValue(object value)
			{
				if (!Equals(this.value, value))
				{
					this.value = value;
					owner.OnPropertyChanged(propertyInfo.Name);
				}
			} // proc SetProposedValue

			private object GetCoreValue()
				=> propertyInfo.GetValue(owner.data);

			public void UpdateCoreValue()
			{
				if (isChanged)
				{
					propertyInfo.SetValue(owner.data, value);

					isChanged = false;
					owner.OnPropertyChanged(propertyInfo.Name + changedSuffix);
				}
			} // proc UpdateCoreValue

			public object Value
			{
				get => isChanged ? value : GetCoreValue();
				set
				{
					if(!Equals(GetCoreValue(), value))
					{
						SetDirty();
						SetProposedValue(value);
					}
				}
			} // prop Value

			public bool IsChanged => isChanged;
		} // class Property

		#endregion

		public event PropertyChangedEventHandler PropertyChanged;

		private readonly object data;
		private readonly Dictionary<string, Property> proposedValues = new Dictionary<string, Property>(StringComparer.Ordinal);
		private bool isDirty;

		public ProposedObject(object data)
		{
			this.data = data ?? throw new ArgumentNullException(nameof(data));
		} // ctor

		private void SetDirty()
		{
			if (!isDirty)
			{
				isDirty = true;
				OnPropertyChanged(nameof(IsDirty));
			}
		} // proc SetDirty

		private void OnPropertyChanged(string propertyName)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

		public void Update()
		{
			if (isDirty)
			{
				foreach (var cur in proposedValues.Values)
					cur.UpdateCoreValue();
				isDirty = false;
				OnPropertyChanged(nameof(IsDirty));
			}
		} // proc Update

		private bool TryGetProperty(string name, out bool changedProperty, out Property property)
		{
			string propertyName;

			if (name.EndsWith(changedSuffix))
			{
				propertyName = name.Substring(0, name.Length - changedSuffix.Length);
				changedProperty = true;
			}
			else
			{
				propertyName = name;
				changedProperty = false;
			}

			if (proposedValues.TryGetValue(propertyName, out property))
				return true;
			else
			{
				var propInfo = data.GetType().GetProperty(propertyName);
				property = new Property(this, propInfo);
				proposedValues.Add(propertyName, property);
				return true;
			}
		} // func TryGetProperty

		public override bool TryGetMember(GetMemberBinder binder, out object result)
		{
			if (TryGetProperty(binder.Name, out var changedProperty, out var property))
			{
				result = changedProperty ? property.IsChanged : property.Value;
				return true;
			}
			else
				return base.TryGetMember(binder, out result);
		} // func TryGetMember

		public override bool TrySetMember(SetMemberBinder binder, object value)
		{
			if (TryGetProperty(binder.Name, out var changedProperty, out var property))
			{
				if (changedProperty)
					return base.TrySetMember(binder, value);
				else
				{
					property.Value = value;
					return true;
				}
			}
			else
				return base.TrySetMember(binder, value);
		} // func TrySetMember

		public bool IsDirty => isDirty;
	} // class ProposedObject
}
