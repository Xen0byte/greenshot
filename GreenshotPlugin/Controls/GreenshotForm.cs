﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using System.Reflection;
using GreenshotPlugin.Core;
using Greenshot.IniFile;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.IO;

namespace GreenshotPlugin.Controls {
	public abstract class GreenshotForm : Form, IGreenshotLanguageBindable {
		private static log4net.ILog LOG = log4net.LogManager.GetLogger(typeof(GreenshotForm));
		private static IDictionary<Type, FieldInfo[]> reflectionCache = new Dictionary<Type, FieldInfo[]>();
		private IComponentChangeService m_changeService;
		private bool isDesignModeLanguageSet = false;
		private bool applyLanguageManually = false;
		private bool storeFieldsManually = false;
		private IDictionary<string, Control> designTimeControls;
		private IDictionary<string, ToolStripItem> designTimeToolStripItems;

		[Category("Greenshot"), DefaultValue(null), Description("Specifies key of the language file to use when displaying the text.")]
		public string LanguageKey {
			get;
			set;
		}

		protected bool ManualLanguageApply {
			get {
				return applyLanguageManually;
			}
			set {
				applyLanguageManually = value;
			}
		}

		protected bool ManualStoreFields {
			get {
				return storeFieldsManually;
			}
			set {
				storeFieldsManually = value;
			}
		}

		/// <summary>
		/// Code to initialize the language etc during design time
		/// </summary>
		protected void InitializeForDesigner() {
			if (this.DesignMode) {
				designTimeControls = new Dictionary<string, Control>();
				designTimeToolStripItems = new Dictionary<string, ToolStripItem>();
				try {
					ITypeResolutionService typeResService = GetService(typeof(ITypeResolutionService)) as ITypeResolutionService;
					
					// Add a hard-path if you are using SharpDevelop
					// Language.AddLanguageFilePath(@"C:\Greenshot\Greenshot\Languages");
					
					// this "type"
					Assembly currentAssembly = this.GetType().Assembly;
					string assemblyPath = typeResService.GetPathOfAssembly(currentAssembly.GetName());
					string assemblyDirectory = Path.GetDirectoryName(assemblyPath);
					if (!Language.AddLanguageFilePath(Path.Combine(assemblyDirectory, @"..\..\Greenshot\Languages\"))) {
						Language.AddLanguageFilePath(Path.Combine(assemblyDirectory, @"..\..\..\Greenshot\Languages\"));
					}
					if (!Language.AddLanguageFilePath(Path.Combine(assemblyDirectory, @"..\..\Languages\"))) {
						Language.AddLanguageFilePath(Path.Combine(assemblyDirectory, @"..\..\..\Languages\"));
					}
				} catch (Exception ex) {
					MessageBox.Show(ex.ToString(), "Greenshot designer exception!");
				}
			}
		}

		/// <summary>
		/// This override is only for the design-time of the form
		/// </summary>
		/// <param name="e"></param>
		protected override void OnPaint(PaintEventArgs e) {
			if (this.DesignMode) {
				if (!isDesignModeLanguageSet) {
					isDesignModeLanguageSet = true;
					try {
						ApplyLanguage();
					} catch (Exception ex) {
						MessageBox.Show(ex.ToString());
					}
				}
			}
			base.OnPaint(e);
		}

		protected override void OnLoad(EventArgs e) {
			if (!this.DesignMode) {
				if (!applyLanguageManually) {
					ApplyLanguage();
				}
				FillFields();
				base.OnLoad(e);
			} else {
				LOG.Info("OnLoad called from designer.");
				InitializeForDesigner();
				base.OnLoad(e);
				ApplyLanguage();
			}
		}

		/// <summary>
		/// check if the form was closed with an OK, if so store the values in the GreenshotControls
		/// </summary>
		/// <param name="e"></param>
		protected override void OnClosed(EventArgs e) {
			if (!this.DesignMode && !storeFieldsManually) {
				if (DialogResult == DialogResult.OK) {
					LOG.Info("Form was closed with OK: storing field values.");
					StoreFields();
				}
			}
			base.OnClosed(e);
		}

		/// <summary>
		/// This override allows the control to register event handlers for IComponentChangeService events
		/// at the time the control is sited, which happens only in design mode.
		/// </summary>
		public override ISite Site {
			get {
				return base.Site;
			}
			set {
				// Clear any component change event handlers.
				ClearChangeNotifications();

				// Set the new Site value.
				base.Site = value;

				m_changeService = (IComponentChangeService)GetService(typeof(IComponentChangeService));

				// Register event handlers for component change events.
				RegisterChangeNotifications();
			}
		}

		private void ClearChangeNotifications() {
			// The m_changeService value is null when not in design mode, 
			// as the IComponentChangeService is only available at design time.	
			m_changeService = (IComponentChangeService)GetService(typeof(IComponentChangeService));

			// Clear our the component change events to prepare for re-siting.				
			if (m_changeService != null) {
				m_changeService.ComponentChanged -= new ComponentChangedEventHandler(OnComponentChanged);
				m_changeService.ComponentAdded -= new ComponentEventHandler(OnComponentAdded);
			}
		}

		private void RegisterChangeNotifications() {
			// Register the event handlers for the IComponentChangeService events
			if (m_changeService != null) {
				m_changeService.ComponentChanged += new ComponentChangedEventHandler(OnComponentChanged);
				m_changeService.ComponentAdded += new ComponentEventHandler(OnComponentAdded);
			}
		}

		/// <summary>
		/// This method handles the OnComponentChanged event to display a notification.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="ce"></param>
		private void OnComponentChanged(object sender, ComponentChangedEventArgs ce) {
			if (ce.Component != null && ((IComponent)ce.Component).Site != null && ce.Member != null) {
				if ("LanguageKey".Equals(ce.Member.Name)) {
					Control control = ce.Component as Control;
					if (control != null) {
						LOG.InfoFormat("Changing LanguageKey for {0} to {1}", control.Name, ce.NewValue);
						ApplyLanguage(control, (string)ce.NewValue);
					} else {
						ToolStripItem item = ce.Component as ToolStripItem;
						if (item != null) {
							LOG.InfoFormat("Changing LanguageKey for {0} to {1}", item.Name, ce.NewValue);
							ApplyLanguage(item, (string)ce.NewValue);
						} else {
							LOG.InfoFormat("Not possible to changing LanguageKey for {0} to {1}", ce.Component.GetType(), ce.NewValue);
						}
					}
				}
			}
		}

		private void OnComponentAdded(object sender, ComponentEventArgs ce) {
			if (ce.Component != null && ((IComponent)ce.Component).Site != null) {
				Control control = ce.Component as Control;
				if (control != null) {
					if (!designTimeControls.ContainsKey(control.Name)) {
						designTimeControls.Add(control.Name, control);
					} else {
						designTimeControls[control.Name] = control;
					}
				} else if (ce.Component is ToolStripItem) {
					ToolStripItem item = ce.Component as ToolStripItem;
					if (!designTimeControls.ContainsKey(item.Name)) {
						designTimeToolStripItems.Add(item.Name, item);
					} else {
						designTimeToolStripItems[item.Name] = item;
					}
				}
			}
		}

		// Clean up any resources being used.
		protected override void Dispose(bool disposing) {
			if (disposing) {
				ClearChangeNotifications();
			}
			base.Dispose(disposing);
		}

		protected void ApplyLanguage(ToolStripItem applyTo, string languageKey) {
			string langString = null;
			if (!string.IsNullOrEmpty(languageKey)) {
				if (!Language.TryGetString(languageKey, out langString)) {
					LOG.WarnFormat("Wrong language key '{0}' configured for control '{1}'", languageKey, applyTo.Name);
					if (DesignMode) {
						MessageBox.Show(string.Format("Wrong language key '{0}' configured for control '{1}'", languageKey, applyTo.Name));
					}
					return;
				}
				applyTo.Text = langString;
			} else {
				// Fallback to control name!
				if (Language.TryGetString(applyTo.Name, out langString)) {
					applyTo.Text = langString;
					return;
				}
				if (this.DesignMode) {
					MessageBox.Show(string.Format("Greenshot control without language key: {0}", applyTo.Name));
				} else {
					LOG.DebugFormat("Greenshot control without language key: {0}", applyTo.Name);
				}
			}
		}

		protected void ApplyLanguage(ToolStripItem applyTo) {
			IGreenshotLanguageBindable languageBindable = applyTo as IGreenshotLanguageBindable;
			if (languageBindable != null) {
				ApplyLanguage(applyTo, languageBindable.LanguageKey);
			}
		}

		protected void ApplyLanguage(Control applyTo) {
			IGreenshotLanguageBindable languageBindable = applyTo as IGreenshotLanguageBindable;
			if (languageBindable == null) {
				// check if it's a menu!
				ToolStrip toolStrip = applyTo as ToolStrip;
				if (toolStrip != null) {
					foreach (ToolStripItem item in toolStrip.Items) {
						ApplyLanguage(item);
					}
				}
				return;
			}

			// Apply language text to the control
			ApplyLanguage(applyTo, languageBindable.LanguageKey);

			// Repopulate the combox boxes
			IGreenshotConfigBindable configBindable = applyTo as IGreenshotConfigBindable;
			GreenshotComboBox comboxBox = applyTo as GreenshotComboBox;
			if (configBindable != null && comboxBox != null) {
				if (!string.IsNullOrEmpty(configBindable.SectionName) && !string.IsNullOrEmpty(configBindable.PropertyName)) {
					IniSection section = IniConfig.GetIniSection(configBindable.SectionName);
					if (section != null) {
						// Only update the language, so get the actual value and than repopulate
						Enum currentValue = (Enum)comboxBox.GetSelectedEnum();
						comboxBox.Populate(section.Values[configBindable.PropertyName].ValueType);
						comboxBox.SetValue(currentValue);
					}
				}
			}
		}
		
		/// <summary>
		/// Helper method to cache the fieldinfo values, so we don't need to reflect all the time!
		/// </summary>
		/// <param name="typeToGetFieldsFor"></param>
		/// <returns></returns>
		private static FieldInfo[] GetCachedFields(Type typeToGetFieldsFor) {
			FieldInfo[] fields = null;
			if (!reflectionCache.TryGetValue(typeToGetFieldsFor, out fields)) {
				fields = typeToGetFieldsFor.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				reflectionCache.Add(typeToGetFieldsFor, fields);
			}
			return fields;
		}

		/// <summary>
		/// Apply all the language settings to the "Greenshot" Controls on this form
		/// </summary>
		protected void ApplyLanguage() {
			string langString = null;
			this.SuspendLayout();
			try {
				// Set title of the form
				if (!string.IsNullOrEmpty(LanguageKey) && Language.TryGetString(LanguageKey, out langString)) {
					this.Text = langString;
				}

				// Reset the text values for all GreenshotControls
				foreach (FieldInfo field in GetCachedFields(this.GetType())) {
					Object controlObject = field.GetValue(this);
					if (controlObject == null) {
						LOG.DebugFormat("No value: {0}", field.Name);
						continue;
					}
					Control applyToControl = controlObject as Control;
					if (applyToControl == null) {
						ToolStripItem applyToItem = controlObject as ToolStripItem;
						if (applyToItem == null) {
							LOG.DebugFormat("No Control or ToolStripItem: {0}", field.Name);
							continue;
						}
						ApplyLanguage(applyToItem);
					} else {
						ApplyLanguage(applyToControl);
					}
				}
	
				if (DesignMode) {
					foreach (Control designControl in designTimeControls.Values) {
						ApplyLanguage(designControl);
					}
					foreach (ToolStripItem designToolStripItem in designTimeToolStripItems.Values) {
						ApplyLanguage(designToolStripItem);
					}
				}
			} finally {
				this.ResumeLayout();
			}
		}

		/// <summary>
		/// Apply the language text to supplied control
		/// </summary>
		protected void ApplyLanguage(Control applyTo, string languageKey) {
			string langString = null;
			if (!string.IsNullOrEmpty(languageKey)) {
				if (!Language.TryGetString(languageKey, out langString)) {
					LOG.WarnFormat("Wrong language key '{0}' configured for control '{1}'", languageKey, applyTo.Name);
					MessageBox.Show(string.Format("Wrong language key '{0}' configured for control '{1}'", languageKey, applyTo.Name));
					return;
				}
				applyTo.Text = langString;
			} else {
				// Fallback to control name!
				if (Language.TryGetString(applyTo.Name, out langString)) {
					applyTo.Text = langString;
					return;
				}
				if (this.DesignMode) {
					MessageBox.Show(string.Format("Greenshot control without language key: {0}", applyTo.Name));
				} else {
					LOG.DebugFormat("Greenshot control without language key: {0}", applyTo.Name);
				}
			}
		}

		/// <summary>
		/// Fill all GreenshotControls with the values from the configuration
		/// </summary>
		protected void FillFields() {
			foreach (FieldInfo field in GetCachedFields(this.GetType())) {
				Object controlObject = field.GetValue(this);
				if (controlObject == null) {
					continue;
				}
				IGreenshotConfigBindable configBindable = controlObject as IGreenshotConfigBindable;
				if (configBindable == null) {
					continue;
				}
				if (!string.IsNullOrEmpty(configBindable.SectionName) && !string.IsNullOrEmpty(configBindable.PropertyName)) {
					IniSection section = IniConfig.GetIniSection(configBindable.SectionName);
					if (section != null) {
						IniValue iniValue = null;
						if (!section.Values.TryGetValue(configBindable.PropertyName, out iniValue)) {
							LOG.WarnFormat("Wrong property '{0}' configured for field '{1}'",configBindable.PropertyName,field.Name);
							continue;
						}

						CheckBox checkBox = controlObject as CheckBox;
						if (checkBox != null) {
							checkBox.Checked = (bool)iniValue.Value;
							checkBox.Enabled = !iniValue.Attributes.FixedValue;
							continue;
						}

						TextBox textBox = controlObject as TextBox;
						if (textBox != null) {
							HotkeyControl hotkeyControl = controlObject as HotkeyControl;
							if (hotkeyControl != null) {
								string hotkeyValue = (string)iniValue.Value;
								if (!string.IsNullOrEmpty(hotkeyValue)) {
									hotkeyControl.SetHotkey(hotkeyValue);
									hotkeyControl.Enabled = !iniValue.Attributes.FixedValue;
								}
								continue;
							}
							textBox.Text = iniValue.ToString();
							textBox.Enabled = !iniValue.Attributes.FixedValue;
							continue;
						} 

						GreenshotComboBox comboxBox = controlObject as GreenshotComboBox;
						if (comboxBox != null) {
							comboxBox.Populate(iniValue.ValueType);
							comboxBox.SetValue((Enum)iniValue.Value);
							comboxBox.Enabled = !iniValue.Attributes.FixedValue;
							continue;
						}
					}
				}
			}
		}

		/// <summary>
		/// Store all GreenshotControl values to the configuration
		/// </summary>
		protected void StoreFields() {
			bool iniDirty = false;
			foreach (FieldInfo field in GetCachedFields(this.GetType())) {
				Object controlObject = field.GetValue(this);
				if (controlObject == null) {
					continue;
				}
				IGreenshotConfigBindable configBindable = controlObject as IGreenshotConfigBindable;
				if (configBindable == null) {
					continue;
				}

				if (!string.IsNullOrEmpty(configBindable.SectionName) && !string.IsNullOrEmpty(configBindable.PropertyName)) {
					IniSection section = IniConfig.GetIniSection(configBindable.SectionName);
					if (section != null) {
						IniValue iniValue = null;
						if (!section.Values.TryGetValue(configBindable.PropertyName, out iniValue)) {
							continue;
						}
						CheckBox checkBox = controlObject as CheckBox;
						if (checkBox != null) {
							iniValue.Value = checkBox.Checked;
							iniDirty = true;
							continue;
						}
						TextBox textBox = controlObject as TextBox;
						if (textBox != null) {
                            HotkeyControl hotkeyControl = controlObject as HotkeyControl;
                            if (hotkeyControl != null) {
                                iniValue.Value = hotkeyControl.ToString();
                                iniDirty = true;
                                continue;
                            }
							iniValue.UseValueOrDefault(textBox.Text);
							iniDirty = true;
							continue;
						}
						GreenshotComboBox comboxBox = controlObject as GreenshotComboBox;
						if (comboxBox != null) {
							iniValue.Value = comboxBox.GetSelectedEnum();
							iniDirty = true;
							continue;
						}
						
					}
				}
			}
			if (iniDirty) {
				IniConfig.Save();
			}
		}
	}
}
