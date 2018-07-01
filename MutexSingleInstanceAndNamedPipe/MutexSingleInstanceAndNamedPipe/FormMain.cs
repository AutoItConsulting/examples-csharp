//  
// Copyright (c) AutoIt Consulting Ltd. All rights reserved.  
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.  
// using System;
//

using System;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace MutexSingleInstanceAndNamedPipe
{
    public partial class FormMain : Form
    {
        private const string MutexName = "MUTEX_SINGLEINSTANCEANDNAMEDPIPE";
        private const string PipeName = "PIPE_SINGLEINSTANCEANDNAMEDPIPE";

        private readonly object _namedPiperServerThreadLock = new object();

        private bool _firstApplicationInstance;

        private Mutex _mutexApplication;

        private NamedPipeServerStream _namedPipeServerStream;
        private NamedPipeXmlPayload _namedPipeXmlPayload;

        /// <summary>
        ///     <inheritdoc />
        /// </summary>
        public FormMain()
        {
            InitializeComponent();
        }

        /// <summary>
        ///     Runs when the quit button clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void buttonQuit_Click(object sender, EventArgs e)
        {
            Close();
        }

        /// <summary>
        ///     Called after the form is closed.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FormMain_FormClosed(object sender, FormClosedEventArgs e)
        {
            // Dispose the named pipe steam
            if (_namedPipeServerStream != null)
            {
                _namedPipeServerStream.Dispose();
            }

            // Close and dispose our mutex.
            if (_mutexApplication != null)
            {
                _mutexApplication.Dispose();
            }
        }

        /// <summary>
        ///     Called when the form is loading.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FormMain_Load(object sender, EventArgs e)
        {
            string[] arguments = Environment.GetCommandLineArgs();

            if (arguments.Length >= 2)
            {
                string arg = arguments[1].ToUpper();

                if (arg == "/?" || arg == "?")
                {
                    var usage = @"MutexSingleInstanceAndNamedPipe [/Close] | [Test option1] [Test option nn]";
                    MessageBox.Show(usage, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Close();
                    return;
                }

                if (arg == "/CLOSE" || arg == "CLOSE")
                {
                    // If we are not the first instance, send a quit message along the pipe
                    if (!IsApplicationFirstInstance())
                    {
                        var namedPipeXmlPayload = new NamedPipeXmlPayload
                        {
                            SignalQuit = true
                        };

                        // Send the message
                        NamedPipeClientSendOptions(namedPipeXmlPayload);
                    }

                    // Stop loading form and quit
                    Close();
                    return;
                }
            }

            // If are the first instance then we start the named pipe server listening and allow the form to load
            if (IsApplicationFirstInstance())
            {
                // Create a new pipe - it will return immediately and async wait for connections
                NamedPipeServerCreateServer();
            }
            else
            {
                // We are not the first instance, send the named pipe message with our payload and stop loading
                var namedPipeXmlPayload = new NamedPipeXmlPayload
                {
                    SignalQuit = false,
                    CommandLineArguments = Environment.GetCommandLineArgs().ToList()
                };

                // Send the message
                NamedPipeClientSendOptions(namedPipeXmlPayload);

                // Stop loading form and quit
                Close();
            }
        }

        /// <summary>
        ///     Checks if this is the first instance of this application. Can be run multiple times.
        /// </summary>
        /// <returns></returns>
        private bool IsApplicationFirstInstance()
        {
            // Allow for multiple runs but only try and get the mutex once
            if (_mutexApplication == null)
            {
                _mutexApplication = new Mutex(true, MutexName, out _firstApplicationInstance);
            }

            return _firstApplicationInstance;
        }

        /// <summary>
        ///     Uses a named pipe to send the currently parsed options to an already running instance.
        /// </summary>
        /// <param name="namedPipePayload"></param>
        private void NamedPipeClientSendOptions(NamedPipeXmlPayload namedPipePayload)
        {
            try
            {
                using (var namedPipeClientStream = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    namedPipeClientStream.Connect(3000); // Maximum wait 3 seconds

                    var xmlSerializer = new XmlSerializer(typeof(NamedPipeXmlPayload));
                    xmlSerializer.Serialize(namedPipeClientStream, namedPipePayload);
                }
            }
            catch (Exception)
            {
                // Error connecting or sending
            }
        }

        /// <summary>
        ///     The function called when a client connects to the named pipe. Note: This method is called on a non-UI thread.
        /// </summary>
        /// <param name="iAsyncResult"></param>
        private void NamedPipeServerConnectionCallback(IAsyncResult iAsyncResult)
        {
            try
            {
                // End waiting for the connection
                _namedPipeServerStream.EndWaitForConnection(iAsyncResult);

                // Read data and prevent access to _namedPipeXmlPayload during threaded operations
                lock (_namedPiperServerThreadLock)
                {
                    // Read data from client
                    var xmlSerializer = new XmlSerializer(typeof(NamedPipeXmlPayload));
                    _namedPipeXmlPayload = (NamedPipeXmlPayload)xmlSerializer.Deserialize(_namedPipeServerStream);

                    // Need to signal quit?
                    if (_namedPipeXmlPayload.SignalQuit)
                    {
                        NamedPipeThreadEvent_Close();
                        return;
                    }

                    // _namedPipeXmlPayload contains the data sent from the other instance
                    // As an example output it to the textbox
                    // In more complicated cases would need to do some processing here and possibly pass to UI thread
                    TextBoxAppend(_namedPipeXmlPayload);
                }
            }
            catch (ObjectDisposedException)
            {
                // EndWaitForConnection will exception when someone calls closes the pipe before connection made
                // In that case we dont create any more pipes and just return
                // This will happen when app is closing and our pipe is closed/disposed
                return;
            }
            catch (Exception)
            {
                // ignored
            }
            finally
            {
                // Close the original pipe (we will create a new one each time)
                _namedPipeServerStream.Dispose();
            }

            // Create a new pipe for next connection
            NamedPipeServerCreateServer();
        }

        /// <summary>
        ///     Starts a new pipe server if one isn't already active.
        /// </summary>
        private void NamedPipeServerCreateServer()
        {
            // Create a new pipe accessible by local authenticated users, disallow network
            //var sidAuthUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            var sidNetworkService = new SecurityIdentifier(WellKnownSidType.NetworkServiceSid, null);
            var sidWorld = new SecurityIdentifier(WellKnownSidType.WorldSid, null);

            var pipeSecurity = new PipeSecurity();

            // Deny network access to the pipe
            var accessRule = new PipeAccessRule(sidNetworkService, PipeAccessRights.ReadWrite, AccessControlType.Deny);
            pipeSecurity.AddAccessRule(accessRule);

            // Alow Everyone to read/write
            accessRule = new PipeAccessRule(sidWorld, PipeAccessRights.ReadWrite, AccessControlType.Allow);
            pipeSecurity.AddAccessRule(accessRule);

            // Current user is the owner
            SecurityIdentifier sidOwner = WindowsIdentity.GetCurrent().Owner;
            if (sidOwner != null)
            {
                accessRule = new PipeAccessRule(sidOwner, PipeAccessRights.FullControl, AccessControlType.Allow);
                pipeSecurity.AddAccessRule(accessRule);
            }

            // Create pipe and start the async connection wait
            _namedPipeServerStream = new NamedPipeServerStream(
                PipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0,
                0,
                pipeSecurity);

            // Begin async wait for connections
            _namedPipeServerStream.BeginWaitForConnection(NamedPipeServerConnectionCallback, _namedPipeServerStream);
        }

        /// <summary>
        ///     Called when a quit request has been received.
        /// </summary>
        private void NamedPipeThreadEvent_Close()
        {
            if (InvokeRequired)
            {
                // Invoke this same function the the UI thread of the form
                Invoke((MethodInvoker)NamedPipeThreadEvent_Close);
                return;
            }

            // Close the form
            Close();
        }

        /// <summary>
        ///     Appends string version of the payload to the end of the text box. Handles being called from a non UI thread.
        /// </summary>
        private void TextBoxAppend(NamedPipeXmlPayload namedPipeXmlPayload)
        {
            if (textBoxOutput.InvokeRequired)
            {
                textBoxOutput.Invoke((MethodInvoker)delegate { TextBoxAppend(namedPipeXmlPayload); });
                return;
            }

            string message = "SignalQuit: " + namedPipeXmlPayload.SignalQuit;

            foreach (string commandLine in namedPipeXmlPayload.CommandLineArguments)
            {
                message += "\r\nCommandLine: " + commandLine;
            }

            textBoxOutput.Text += message + "\r\n\r\n";
        }
    }
}