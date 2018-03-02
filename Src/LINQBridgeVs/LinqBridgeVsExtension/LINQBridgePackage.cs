﻿#region License
// Copyright (c) 2013 - 2018 Giovanni Campo
//
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.
#endregion

using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Windows;
using EnvDTE;
using EnvDTE80;
using BridgeVs.Helper;
using BridgeVs.Helper.Forms;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using BridgeVs.Logging;
using BridgeVs.Helper.Configuration;
using System.Security.Principal;
using System.Globalization;
using System.Diagnostics;

namespace BridgeVs.Extension
{
    /// <inheritdoc />
    ///  <summary>
    ///  This is the class that implements the package exposed by this assembly.
    ///  The minimum requirement for a class to be considered a valid package for Visual Studio
    ///  is to implement the IVsPackage interface and register itself with the shell.
    ///  This package uses the helper classes defined inside the Managed Package Framework (MPF)
    ///  to do it: it derives from the Package class that provides the implementation of the 
    ///  IVsPackage interface and uses the registration attributes defined in the framework to 
    ///  register itself and its components with the shell.
    ///  </summary>
    [PackageRegistration(UseManagedResourcesOnly = true)]
    // This attribute is used to register the information needed to show this package
    // in the Help/About dialog of Visual Studio.
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    // This attribute is needed to let the shell know that this package exposes some menus.
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.EmptySolution_string)]
    [Guid(GuidList.GuidBridgeVsExtensionPkgString)]
    public sealed class LINQBridgeVsPackage : Package
    {
        private DTE _dte;
        private const string VisualStudioProcessName = "devenv";

        public static bool IsElevated
        {
            get
            {
                return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        #region Package Members

        /// <inheritdoc />
        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            Stopwatch watch = new Stopwatch();
            watch.Start();
            base.Initialize();

            _dte = (DTE)GetService(typeof(SDTE));

            var bridge = new LINQBridgeVsExtension(_dte);
            bool isLinqBridgeVsConfigured = PackageConfigurator.IsLINQBridgeVsConfigured;

            // Add our command handlers for menu(commands must exist in the.vsct file)
            var mcs = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (null == mcs) return;

            // Create the command for the menu item.
            var enableCommand = new CommandID(GuidList.GuidBridgeVsExtensionCmdSet, (int)PkgCmdIdList.CmdIdEnableBridge);
            var menuItemEnable = new OleMenuCommand((s, e) => bridge.Execute(CommandAction.Enable), enableCommand);
            menuItemEnable.BeforeQueryStatus += (s, e) => bridge.UpdateCommand(menuItemEnable, CommandAction.Enable);

            var disableCommand = new CommandID(GuidList.GuidBridgeVsExtensionCmdSet,
                (int)PkgCmdIdList.CmdIdDisableBridge);
            var menuItemDisable = new OleMenuCommand((s, e) => bridge.Execute(CommandAction.Disable), disableCommand);
            menuItemDisable.BeforeQueryStatus += (s, e) => bridge.UpdateCommand(menuItemDisable, CommandAction.Disable);

            var aboutCommand = new CommandID(GuidList.GuidBridgeVsExtensionCmdSet, (int)PkgCmdIdList.CmdIdAbout);
            var menuItemAbout = new OleMenuCommand((s, e) => { var about = new About(); about.ShowDialog(); }, aboutCommand);

            mcs.AddCommand(menuItemEnable);
            mcs.AddCommand(menuItemDisable);
            mcs.AddCommand(menuItemAbout);

            try
            {
             //   Log.Configure("LINQBridgeVs", "Extensions");
                 
                //if first time user 
                if (PackageConfigurator.IsLINQBridgeVsConfigured)
                {
                    return;
                }

                if (!IsElevated)
                {
                    if (Application.ResourceAssembly == null)
                        Application.ResourceAssembly = typeof(Welcome).Assembly;

                    Welcome welcomePage = new Welcome(_dte);
                    welcomePage.Show();
                }
                else
                {
                    if (!PackageConfigurator.Install(_dte.Version, _dte.Edition))
                    {
                        MessageBox.Show("LINQBridgeVs configuration wasn't successful. Please restart Visual Studio");
                    }
                }
            }
            catch (Exception e)
            {
                //Log.Write(e, "OnStartupComplete Error...");
            }
            watch.Stop();
            var mill = watch.ElapsedMilliseconds;
        }
         
        #endregion
    }
}