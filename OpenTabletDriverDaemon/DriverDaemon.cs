﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using TabletDriverLib;
using TabletDriverLib.Binding;
using TabletDriverLib.Contracts;
using TabletDriverLib.Plugins;
using TabletDriverPlugin;
using TabletDriverPlugin.Tablet;

namespace OpenTabletDriverDaemon
{
    public class DriverDaemon : IDriverDaemon
    {
        public DriverDaemon()
        {
            Driver = new Driver();
            LoadUserSettings();
        }

        private async void LoadUserSettings()
        {
            await LoadPlugins();
            DetectTablets();

            if (Settings == null && AppInfo.SettingsFile.Exists)
            {
                var settings = Settings.Deserialize(AppInfo.SettingsFile);
                SetSettings(settings);
            }
        }

        public Driver Driver { private set; get; }
        private Settings Settings { set; get; }
        private Collection<FileInfo> LoadedPlugins { set; get; } = new Collection<FileInfo>();
        private TabletDebuggerServer TabletDebuggerServer { set; get; }
        private TabletDebuggerServer AuxDebuggerServer { set; get; }

        private const string TabletDebuggerPipe = "OpenTabletDriver_TABLETDEBUGGER";
        private const string AuxDebuggerPipe = "OpenTabletDriver_AUXDEBUGGER";

        public bool SetTablet(TabletProperties tablet)
        {
            return Driver.Open(tablet);
        }

        public TabletProperties GetTablet()
        {
            return Driver.TabletProperties;
        }

        public TabletProperties DetectTablets()
        {
            if (AppInfo.ConfigurationDirectory.Exists)
            {
                foreach (var file in AppInfo.ConfigurationDirectory.EnumerateFiles("*.json", SearchOption.AllDirectories))
                {
                    var tablet = TabletProperties.Read(file);
                    if (SetTablet(tablet))
                        return GetTablet();
                }
            }
            return null;
        }

        public void SetSettings(Settings settings)
        {
            Settings = settings;
            
            Driver.OutputMode = new PluginReference(Settings.OutputMode).Construct<IOutputMode>();

            if (Driver.OutputMode != null)
            {
                Log.Write("Settings", $"Output mode: {Driver.OutputMode.GetType().FullName}");
            }

            if (Driver.OutputMode is IOutputMode outputMode)
            {                
                outputMode.Filters = from filter in Settings?.Filters
                    select new PluginReference(filter).Construct<IFilter>();

                if (outputMode.Filters != null)
                    Log.Write("Settings", $"Filters: {string.Join(", ", outputMode.Filters)}");
                
                outputMode.TabletProperties = Driver.TabletProperties;
            }
            
            if (Driver.OutputMode is IAbsoluteMode absoluteMode)
            {
                absoluteMode.Output = new Area
                {
                    Width = Settings.DisplayWidth,
                    Height = Settings.DisplayHeight,
                    Position = new Point
                    {
                        X = Settings.DisplayX,
                        Y = Settings.DisplayY
                    }
                };
                Log.Write("Settings", $"Display area: {absoluteMode.Output}");

                absoluteMode.Input = new Area
                {
                    Width = Settings.TabletWidth,
                    Height = Settings.TabletHeight,
                    Position = new Point
                    {
                        X = Settings.TabletX,
                        Y = Settings.TabletY
                    }
                };
                Log.Write("Settings", $"Tablet area: {absoluteMode.Input}");

                absoluteMode.AreaClipping = Settings.EnableClipping;   
                Log.Write("Settings", $"Clipping: {(absoluteMode.AreaClipping ? "Enabled" : "Disabled")}");
            }

            if (Driver.OutputMode is IRelativeMode relativeMode)
            {
                relativeMode.XSensitivity = Settings.XSensitivity;
                Log.Write("Settings", $"Horizontal Sensitivity: {relativeMode.XSensitivity}");

                relativeMode.YSensitivity = Settings.YSensitivity;
                Log.Write("Settings", $"Vertical Sensitivity: {relativeMode.YSensitivity}");

                relativeMode.ResetTime = Settings.ResetTime;
                Log.Write("Settings", $"Reset time: {relativeMode.ResetTime}");
            }

            if (Driver.OutputMode is IBindingHandler<IBinding> bindingHandler)
            {
                bindingHandler.TipBinding = BindingTools.GetBinding(Settings.TipButton);
                bindingHandler.TipActivationPressure = Settings.TipActivationPressure;
                Log.Write("Settings", $"Tip Binding: '{bindingHandler.TipBinding?.Name ?? "None"}'@{bindingHandler.TipActivationPressure}%");

                if (Settings.PenButtons != null)
                {
                    for (int index = 0; index < Settings.PenButtons.Count; index++)
                        bindingHandler.PenButtonBindings[index] = BindingTools.GetBinding(Settings.PenButtons[index]);

                    Log.Write("Settings", $"Pen Bindings: " + string.Join(", ", bindingHandler.PenButtonBindings));
                }

                if (Settings.AuxButtons != null)
                {
                    for (int index = 0; index < Settings.AuxButtons.Count; index++)
                        bindingHandler.AuxButtonBindings[index] = BindingTools.GetBinding(Settings.AuxButtons[index]);

                    Log.Write("Settings", $"Express Key Bindings: " + string.Join(", ", bindingHandler.AuxButtonBindings));
                }
            }

            if (Settings.AutoHook)
            {
                Driver.BindingEnabled = true;
                Log.Write("Settings", "Driver is auto-enabled.");
            }
        }

        public Settings GetSettings()
        {
            return Settings;
        }

        public async Task<bool> LoadPlugins()
        {
            if (AppInfo.PluginDirectory.Exists)
            {
                foreach (var file in AppInfo.PluginDirectory.EnumerateFiles("*.dll", SearchOption.AllDirectories))
                    await ImportPlugin(file.FullName);
                return true;
            }
            else
                return false;
        }

        public async Task<bool> ImportPlugin(string pluginPath)
        {
            if (LoadedPlugins.Any(p => p.FullName == pluginPath))
            {
                return true;
            }
            else
            {
                var plugin = new FileInfo(pluginPath);
                LoadedPlugins.Add(plugin);
                return await PluginManager.AddPlugin(plugin);
            }
        }

        public void SetInputHook(bool isHooked)
        {
            Driver.BindingEnabled = isHooked;
        }

        public void SetTabletDebug(bool isEnabled)
        {
            if (isEnabled && TabletDebuggerServer == null)
            {
                TabletDebuggerServer = new TabletDebuggerServer(TabletDebuggerPipe);
                Driver.TabletReader.Report += TabletDebuggerServer.HandlePacket;
                
                if (Driver.AuxReader != null)
                {
                    AuxDebuggerServer = new TabletDebuggerServer(AuxDebuggerPipe);
                    Driver.AuxReader.Report += AuxDebuggerServer.HandlePacket;
                }
            }
            else if (!isEnabled && TabletDebuggerServer != null)
            {
                Driver.TabletReader.Report -= TabletDebuggerServer.HandlePacket;
                TabletDebuggerServer.Dispose();
                TabletDebuggerServer = null;
                
                if (Driver.AuxReader != null)
                {
                    Driver.AuxReader.Report -= AuxDebuggerServer.HandlePacket;
                    AuxDebuggerServer.Dispose();
                    AuxDebuggerServer = null;
                }
            }
        }

        public IEnumerable<string> GetChildTypes<T>()
        {
            return from type in PluginManager.GetChildTypes<T>()
                select type.FullName;
        }
    }
}