using Microsoft.Kinect;
using Microsoft.Kinect.Input;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Fleck;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Automation;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HandControllerServer
{
    class Program
    {
        /// <summary>
        /// Active Kinect sensor
        /// </summary>
        static KinectSensor kinectSensor = null;

        /// <summary>
        /// Reader for body frames
        /// </summary>
        static BodyFrameReader bodyFrameReader = null;

        /// <summary>
        /// Array for the bodies
        /// </summary>
        static Body[] bodies = null;

        /// <summary>
        /// Keeps track of last time, so we know when we get a new set of pointers. Pointer events fire multiple times per timestamp, based on how
        /// many pointers are present
        /// </summary>
        static TimeSpan lastTime;

        /// <summary>
        /// Engaged hand
        /// </summary>
        static Hand hand;

        /// <summary>
        /// Type of the engaged hand
        /// </summary>
        static HandType engagedHandType;

        /// <summary>
        /// Id of the currently engaged body
        /// </summary>
        static ulong engagedBodyId;

        /// <summary>
        /// List of connected clients
        /// </summary>
        static List<IWebSocketConnection> clients = new List<IWebSocketConnection>();

        /// <summary>
        /// Stream to store json data
        /// </summary>
        static MemoryStream memStream;

        /// <summary>
        /// Json serializaer
        /// </summary>
        static DataContractJsonSerializer serializer;

        /// <summary>
        /// Stream reader to read json stream
        /// </summary>
        static StreamReader streamReader;

#if DEBUG
        /// <summary>
        /// Number of messages sended in one second
        /// </summary>
        static int msgsSentCounter = 0;

        /// <summary>
        /// Time counter
        /// </summary>
        static Stopwatch time = new Stopwatch();
#endif

        [DllImport("kernel32.dll", ExactSpelling = true)]
        public static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        const int CONSOLE_HIDE = 0;
        const int CONSOLE_SHOW = 5;
        
        /// <summary>
        /// Main method
        /// </summary>
        static void Main(string[] args)
        {
#if DEBUG
            ShowWindow(GetConsoleWindow(), CONSOLE_SHOW);
#else
            ShowWindow(GetConsoleWindow(), CONSOLE_HIDE);
#endif
            hand = new Hand();
            hand.closed = false;
            engagedBodyId = 0;
            engagedHandType = HandType.NONE;
            memStream = new MemoryStream();
            serializer = new DataContractJsonSerializer(typeof(Hand));
            streamReader = new StreamReader(memStream);

            try
            {
                InitializeConnection();
                InitilizeKinect();

                AutomationFocusChangedEventHandler focusHandler = OnFocusChange;
                Automation.AddAutomationFocusChangedEventHandler(focusHandler);

                MessageBox.Show("The program is running in the background, you can now controll nunuStudio´s activities with your hand. \n"
                    + "This program will always be on focus, so it can be annoying when working with other applications. \n"
                    + "To finish it, just close this window. The most easy way is to right click on this window in the taskbar and click close.", "Kinect Hand Motion",
                    MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
                Automation.RemoveAutomationFocusChangedEventHandler(focusHandler);
            }
            catch (Exception)
            {
                System.Environment.Exit(1);
            }

            Stop();

            MessageBox.Show(new Form() { TopMost = true }, "Closed Successfully", "Kinect Hand Motion",
                MessageBoxButtons.OK, MessageBoxIcon.Information);

        }

        /// <summary>
        /// Execute shutdown tasks
        /// </summary>
        private static void Stop()
        {
            if (bodyFrameReader != null)
            {
                bodyFrameReader.Dispose();
                bodyFrameReader = null;
            }

            if (kinectSensor != null)
            {
                kinectSensor.Close();
                kinectSensor = null;
            }
        }

        /// <summary>
        /// Always put this application with focus, to be able to receive pointer input from kinect
        /// </summary>
        /// <param name="source">object sending the event</param>
        /// <param name="e">event arguments</param>
        private static void OnFocusChange(object source, AutomationFocusChangedEventArgs e)
        {
            var focusedHandle = new IntPtr(AutomationElement.FocusedElement.Current.NativeWindowHandle);
            var myConsoleHandle = GetConsoleWindow();

            if (focusedHandle != myConsoleHandle)
            {
                SetForegroundWindow(myConsoleHandle);
            }
        }

        /// <summary>
        /// Open the web socket and deals with connections
        /// </summary>
        private static void InitializeConnection()
        {
            var server = new WebSocketServer("ws://127.0.0.1:8181");

            server.Start(socket =>
            {
                socket.OnOpen = () =>
                {
                    clients.Add(socket);
                };

                socket.OnClose = () =>
                {
                    clients.Remove(socket);
                };
            });
        }

        /// <summary>
        /// Initializes the kinect sensor
        /// </summary>
        private static void InitilizeKinect()
        {
            kinectSensor = KinectSensor.GetDefault();
            bodyFrameReader = kinectSensor.BodyFrameSource.OpenReader();
            bodyFrameReader.FrameArrived += Reader_FrameArrived;

            KinectCoreWindow kinectCoreWindow = KinectCoreWindow.GetForCurrentThread();
            kinectCoreWindow.PointerMoved += KinectCoreWindow_PointerMoved;

            kinectSensor.Open();

#if DEBUG
            time.Start();
#endif
        }

        /// <summary>
        /// Handles the body frame data arriving from the sensor
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private static void Reader_FrameArrived(object sender, BodyFrameArrivedEventArgs e)
        {
            bool dataReceived = false;

            using (BodyFrame bodyFrame = e.FrameReference.AcquireFrame())
            {
                if (bodyFrame != null)
                {
                    if (bodies == null)
                    {
                        bodies = new Body[bodyFrame.BodyCount];
                    }
                    
                    bodyFrame.GetAndRefreshBodyData(bodies);
                    dataReceived = true;
                }
            }

            if (dataReceived)
            {
                for (int i = 0; i < bodies.Length; i++)
                {
                    Body body = bodies[i];

                    if (body.IsTracked && body.TrackingId == engagedBodyId)
                    {
                        if (engagedHandType == HandType.LEFT)
                        {
                            processHandState(body.HandLeftState);
                        }
                        else if (engagedHandType == HandType.RIGHT)
                        {
                            processHandState(body.HandRightState);
                        }
                        else
                        {
                            processHandStateClosed(false);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Process the state of the currently engaged hand
        /// </summary>
        /// <param name="state">state of the currently engaged hand</param>
        private static void processHandState(HandState state)
        {
            if (state == HandState.Closed)
            {
                processHandStateClosed(true);
            }
            else
            {
                processHandStateClosed(false);
            }
        }

        /// <summary>
        /// Update the closed state of the engaged hand, if it is not already in the updated state
        /// </summary>
        /// <param name="closed">indicates if the engaged hand is closed</param>
        private static void processHandStateClosed(bool closed)
        {
            if (hand.closed != closed)
            {
                hand.closed = closed;
                SendPointerInfo();
            }
        }

        /// <summary>
        /// Handles kinect pointer events
        /// </summary>
        /// <param name="sender">the KinectCoreWindow</param>
        /// <param name="args">Kinect pointer args</param>
        private static void KinectCoreWindow_PointerMoved(object sender, KinectPointerEventArgs args)
        {
            KinectPointerPoint kinectPointerPoint = args.CurrentPoint;
            if (lastTime == TimeSpan.Zero || lastTime != kinectPointerPoint.Properties.BodyTimeCounter)
            {
                lastTime = kinectPointerPoint.Properties.BodyTimeCounter;
            }

            if (kinectPointerPoint.Properties.IsEngaged)
            {
                hand.posX = kinectPointerPoint.Position.X;
                hand.posY = kinectPointerPoint.Position.Y;
                engagedBodyId = kinectPointerPoint.Properties.BodyTrackingId;
                engagedHandType = kinectPointerPoint.Properties.HandType;
                SendPointerInfo();
            }
        }

        /// <summary>
        /// Send to all clients, the engaged hand information in json
        /// </summary>
        private static void SendPointerInfo()
        {
            memStream.SetLength(0);
            serializer.WriteObject(memStream, hand);
            memStream.Position = 0;

            string jsonString = streamReader.ReadToEnd();

            foreach (var socket in clients)
            {
                socket.Send(jsonString);
            }

#if DEBUG
            if (time.ElapsedMilliseconds >= 1000L)
            {
                Console.WriteLine(msgsSentCounter);
                msgsSentCounter = 0;
                time.Restart();
            }
            msgsSentCounter++;
#endif
        }
    }

    /// <summary>
    /// Structure that represents the engaged hand info to be sent to the clients
    /// </summary>
    [DataContract]
    internal class Hand
    {
        [DataMember]
        internal bool closed;

        [DataMember]
        internal float posX;

        [DataMember]
        internal float posY;
    }
}
