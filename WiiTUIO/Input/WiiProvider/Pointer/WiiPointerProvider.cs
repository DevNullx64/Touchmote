﻿using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using WiimoteLib;
using System.Runtime.InteropServices;
using System.Drawing;
using WindowsInput;
using WiiTUIO.Properties;
using System.Windows.Controls;

namespace WiiTUIO.Provider
{
    /// <summary>
    /// The WiiProvider implements <see cref="IProvider"/> in order to offer a type of object which uses the Wiimote to generate new event frames.
    /// </summary>
    public class WiiPointerProvider : IProvider
    {

        private DuoTouch duoTouch;

        private SmoothingBuffer smoothingBuffer;

        private UserControl settingsControl = null;

        private string cursor = "Resources/touchcursor.cur";

        private bool changeSystemCursor = false;

        private bool MouseMode = false;

        private bool mouseWait = false;

        private int TouchHoldThreshold = 10;

        private WiimoteLib.Point FirstTouch = new WiimoteLib.Point();

        private bool TouchHold = true;

        private bool isFirstTouch = true;

        private WiiKeyMapper keyMapper;

        private WiimoteLib.Point lastpoint;

        private bool touchDownMaster = false;

        private bool touchDownSlave = false;

        #region CalibrationRectangle
        /// <summary>
        /// The CalibrationRectangle class defines a set of 4 2D coordinates that define a rectangle in absolute space.
        /// These are used as inputs that define transform the WiiProvider applies to any inputs from the Wiimote device.
        /// </summary>
        [Serializable]
        public class CalibrationRectangle
        {
            /// <summary>The top left corner of a rectangle in absolute coordinates.</summary>
            public Vector TopLeft;
            /// <summary>The top right corner of a rectangle in absolute coordinates.</summary>
            public Vector TopRight;
            /// <summary>The bottom left corner of a rectangle in absolute coordinates.</summary>
            public Vector BottomLeft;
            /// <summary>The bottom right corner of a rectangle in absolute coordinates.</summary>
            public Vector BottomRight;

            /// <summary>
            /// Construct a new CalibrationRectangle with dimension information.
            /// </summary>
            /// <param name="vTopLeft">The top left corner of a rectangle in absolute coordinates.</param>
            /// <param name="vTopRight">The top right corner of a rectangle in absolute coordinates.</param>
            /// <param name="vBottomLeft">The bottom left corner of a rectangle in absolute coordinates.</param>
            /// <param name="vBottomRight">The bottom right corner of a rectangle in absolute coordinates.</param>
            public CalibrationRectangle(Vector vTopLeft, Vector vTopRight, Vector vBottomLeft, Vector vBottomRight)
            {
                this.TopLeft = vTopLeft;
                this.TopRight = vTopRight;
                this.BottomLeft = vBottomLeft;
                this.BottomRight = vBottomRight;
            }

            /// <summary>
            /// Construct a new CalibrationRectangle with dimension information.
            /// </summary>
            /// <param name="x0">The x-coordinate of the top left corner of a rectangle in absolute coordinates.</param>
            /// <param name="y0">The y-coordinate of the top left corner of a rectangle in absolute coordinates.</param>
            /// <param name="x1">The x-coordinate of the top right corner of a rectangle in absolute coordinates.</param>
            /// <param name="y1">The y-coordinate of the top right corner of a rectangle in absolute coordinates.</param>
            /// <param name="x2">The x-coordinate of the bottom left corner of a rectangle in absolute coordinates.</param>
            /// <param name="y2">The y-coordinate of the bottom left corner of a rectangle in absolute coordinates.</param>
            /// <param name="x3">The x-coordinate of the bottom right corner of a rectangle in absolute coordinates.</param>
            /// <param name="y3">The y-coordinate of the bottom right corner of a rectangle in absolute coordinates.</param>
            public CalibrationRectangle(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3)
            {
                this.TopLeft = new Vector(x0, y0);
                this.TopRight = new Vector(x1, y1);
                this.BottomLeft = new Vector(x2, y2);
                this.BottomRight = new Vector(x3, y3);
            }

            /// <summary>
            /// Construct a new CalibrationRectangle with default 0-1 coordinates.
            /// </summary>
            public CalibrationRectangle()
                : this(0.0, 0.0, 1.0, 0.0, 0.0, 1.0, 1.0, 1.0)
            {
            }
        }
        #endregion

        #region Properties and Constructor
        /// <summary>
        /// Boolean which indicates if we are generating input or not.
        /// </summary>
        private bool bRunning = false;

        /// <summary>
        /// A queue for the frame of events.
        /// </summary>
        //private Queue<WiiContact> lFrame = new Queue<WiiContact>(1);

        /// <summary>
        /// A refrence to our wiimote device.
        /// </summary>
        private Wiimote pDevice = null;

        /// <summary>
        /// Used to obtain mutual exlusion over Wiimote updates.
        /// </summary>
        private Mutex pDeviceMutex = new Mutex();

        /// <summary>
        /// An input classifier which we will use to organise points.
        /// </summary>
        public SpatioTemporalClassifier InputClassifier { get; protected set; }

        /// <summary>
        /// The screen size that we use for normalising coordinates.
        /// </summary>
        public Vector ScreenSize { get; protected set; }

        /// <summary>
        /// A property to determine if this input provider is running (and thus generating events).
        /// </summary>
        public bool IsRunning { get { return this.bRunning; } }

        /// <summary>
        /// Do we want to use the calibration transformation step when generating input.
        /// </summary>
        public bool TransformResults { get; set; }

        /// <summary>
        /// This defines an event which is raised when a new frame of touch events is prepared and ready to be dispatched by this provider.
        /// </summary>
        public event EventHandler<FrameEventArgs> OnNewFrame;

        #region Battery State
        /// <summary>
        /// An event which is fired when the battery state changes.
        /// </summary>
        public event Action<int> OnBatteryUpdate;


        public event Action<int> OnConnect;
        public event Action<int> OnDisconnect;

        /// <summary>
        /// The internal battery state.
        /// </summary>
        private int iBatteryState = 0;

        /// <summary>
        /// Get the current battery state.
        /// </summary>
        public int BatteryState
        {
            get
            {
                return iBatteryState;
            }
            protected set
            {
                if (value != iBatteryState)
                {
                    iBatteryState = value;
                    if (OnBatteryUpdate != null)
                        OnBatteryUpdate(iBatteryState);
                }
            }
        }
        #endregion

        /// <summary>
        /// Construct a new wiimote provider.
        /// </summary>
        public WiiPointerProvider()
        {

            lastpoint = new WiimoteLib.Point();
            lastpoint.X = 0;
            lastpoint.Y = 0;

            Settings.Default.SettingChanging += SettingChanging;

            this.settingsControl = new WiiPointerProviderSettings();

            this.ScreenSize = new Vector(Util.ScreenWidth, Util.ScreenHeight);

            this.smoothingBuffer = new SmoothingBuffer(3);

            this.duoTouch = new DuoTouch(ScreenSize, 3);

            this.keyMapper = new WiiKeyMapper();

            this.keyMapper.OnButtonDown += WiiButton_Down;
            this.keyMapper.OnButtonUp += WiiButton_Up;
        }

        private void WiiButton_Up(WiiButtonEvent evt)
        {
            if (evt.Action.ToLower() == "mousetoggle" && !evt.Handled)
            {
                this.MouseMode = this.MouseMode ? false : true;
            }
            if (evt.Action.ToLower() == "touchmaster" && !evt.Handled)
            {
                touchDownMaster = false;
            }
            if (evt.Action.ToLower() == "touchslave" && !evt.Handled)
            {
                touchDownSlave = false;
            }
        }

        private void WiiButton_Down(WiiButtonEvent evt)
        {
            if (evt.Action.ToLower() == "touchmaster" && !evt.Handled)
            {
                touchDownMaster = true;
            }
            if (evt.Action.ToLower() == "touchslave" && !evt.Handled)
            {
                touchDownSlave = true;
            }
        }


        private void SettingChanging(object sender, System.Configuration.SettingChangingEventArgs e)
        {
            
        }
        #endregion

        #region Start and Stop

        /// <summary>
        /// Instructs this input provider to begin generating events.
        /// </summary>
        public void start()
        {
            // Ensure we cannot process any events.
            //pDeviceMutex.WaitOne();

            // Create a new reference to a wiimote device.
            Exception pError = null;
            if (!this.initialiseWiimoteConnection(out pError))
            {
                //pDeviceMutex.ReleaseMutex();
                throw new Exception("Could not establish connection to Wiimote: " + pError.Message, pError);
            }


            // Set the running flag.
            this.bRunning = true;

            /*
            this.changeSystemCursor = Settings.Default.pointer_changeSystemCursor;

            if (this.changeSystemCursor)
            {
                try
                {
                    MouseSimulator.SetSystemCursor(cursor);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
            }
            */

            OnConnect(1);

            // Release processing.
            //pDeviceMutex.ReleaseMutex();
        }

        /// <summary>
        /// Instructs this input provider to stop generating events.
        /// </summary>
        public void stop()
        {
            // Ensure we cannot process any events.
            pDeviceMutex.WaitOne();
            
            // Set the running flag.
            this.bRunning = false;
            
            this.teardownWiimoteConnection();


            // Release processing.
            pDeviceMutex.ReleaseMutex();

            //MouseSimulator.ResetSystemCursor();


            OnDisconnect(1);
        }
        #endregion

        #region Connection creation and teardown.
        /// <summary>  
        /// This method creates and sets up our connection to our class-gloal Wiimote device.
        /// This destroys any existing connection before creating a new one.
        /// </summary>
        /// <param name="pErrorReport">A reference to an exception which we want to contain our error if one happened.</param>
        private bool initialiseWiimoteConnection(out Exception pErrorReport)
        {
            // If we have an existing device, teardown the connection.
            this.teardownWiimoteConnection();

            // Check we have a valid device.
            if (this.pDevice == null)
                this.pDevice = new Wiimote();

            // Hook up device event handlers.
            this.pDevice.WiimoteChanged += new EventHandler<WiimoteChangedEventArgs>(handleWiimoteChanged);
            this.pDevice.WiimoteExtensionChanged += new EventHandler<WiimoteExtensionChangedEventArgs>(handleWiimoteExtensionChanged);

            // Try to establish a connection, enable the IR reader and flag some LEDs.
            try
            {
                this.pDevice.Connect();
                this.pDevice.SetReportType(InputReport.IRAccel, true);
                this.pDevice.SetRumble(true);
                this.pDevice.SetLEDs(true, false, false, true);

            }

            // If something went wrong - notify the user..
            catch (Exception pError)
            {
                
                // Ensure we are ok.
                try
                {
                    this.teardownWiimoteConnection();
                }
                finally { }
                // Say we screwed up.
                pErrorReport = pError;
                //throw new Exception("Error establishing connection: " + , pError);
                return false;
            }
            new Timer(stopRumble, null, 80, 0); //Stop the rumble after 80ms
            pErrorReport = null;
            return true;
        }

        /// <summary>
        /// This method destroys our connection to our class-global Wiimote device.
        /// </summary>
        private void teardownWiimoteConnection()
        {
            // If we don't have a device then do nothing.
            if (this.pDevice == null)
                return;

            this.pDevice.SetLEDs(false, false, false, false);
            this.pDevice.SetRumble(false);

            // Close the connection and dispose of the device.
            this.pDevice.Disconnect();
            this.pDevice.Dispose();
            this.pDevice = null;
        }
        #endregion

        private void stopRumble(Object nothing)
        {
            if (this.pDevice != null)
            {
                while (this.pDevice.WiimoteState.Rumble) //Sometimes the Wiimote does not disable the rumble on the first try
                {
                    this.pDevice.SetRumble(false);
                    System.Threading.Thread.Sleep(20);
                }
            }
        }

        #region Wiimote Event Handlers
        /// <summary>
        /// This is called when an extension is attached or unplugged.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void handleWiimoteExtensionChanged(object sender, WiimoteExtensionChangedEventArgs e)
        {
            // Check we have a valid device.
            if (this.pDevice == null)
                return;
            /*
            // If an extension is attached at runtime we want to enable it.
            if (e.Inserted)
                this.pDevice.SetReportType(InputReport.IRExtensionAccel, true);
            else
                this.pDevice.SetReportType(InputReport.IRAccel, true);
             * */
        }

        /// <summary>
        /// This is called when the state of the wiimote changes and a new state report is available.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void handleWiimoteChanged(object sender, WiimoteChangedEventArgs e)
        {
            // Obtain mutual excluseion.
            pDeviceMutex.WaitOne();

            // If we not running then leave.
            if (!bRunning)
            {
                pDeviceMutex.ReleaseMutex();
                return;
            }
            Queue<WiiContact> lFrame = new Queue<WiiContact>(1);
            // Store the state.
            WiimoteState pState = e.WiimoteState;

            // Contain active sensor data.
            List<SpatioTemporalInput> lInputs = new List<SpatioTemporalInput>();

            bool pointerOutOfReach = false;

            WiimoteLib.Point newpoint = lastpoint;

            newpoint = ScreenPositionCalculator.GetPosition(e);

            if (newpoint.X < 0 || newpoint.Y < 0)
            {
                newpoint = lastpoint;
                pointerOutOfReach = true;
                duoTouch.disableHover();
            }
            else if (Settings.Default.pointer_moveCursor)
            {
                duoTouch.enableHover();
            }

            //Temporary solution to the "diamond cursor" problem.
            /*
            if (this.changeSystemCursor)
            {
                try
                {
                    MouseSimulator.RefreshMainCursor();
                }
                catch (Exception error)
                {
                    Console.WriteLine(error.ToString());
                }
            }
            */
            WiimoteState ws = e.WiimoteState;

            keyMapper.processButtonState(ws.ButtonState);

            if (!MouseMode)
            {
                if (this.touchDownMaster)
                {
                    duoTouch.setContactMaster();
                }
                else
                {
                    duoTouch.releaseContactMaster();
                    TouchHold = true;
                }

                duoTouch.setMasterPosition(new System.Windows.Point(newpoint.X, newpoint.Y));

                if (this.touchDownSlave)
                {
                    duoTouch.setSlavePosition(new System.Windows.Point(newpoint.X, newpoint.Y));
                    duoTouch.setContactSlave();
                }
                else
                {
                    duoTouch.releaseContactSlave();
                }

                lastpoint = newpoint;

                lFrame = duoTouch.getFrame();

                FrameEventArgs pFrame = new FrameEventArgs((ulong)Stopwatch.GetTimestamp(), lFrame);

                this.OnNewFrame(this, pFrame);

            }
            else //Mouse mode
            {
                MouseSimulator.SetCursorPosition(newpoint.X, newpoint.Y);
            }

            this.BatteryState = (pState.Battery > 0xc8 ? 0xc8 : (int)pState.Battery);

            // Release mutual exclusion.
            pDeviceMutex.ReleaseMutex();

        }
        #endregion

        public UserControl getSettingsControl()
        {
            return this.settingsControl;
        }
    }
}
