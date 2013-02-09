﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using WiimoteLib;
using WindowsInput;
using WindowsInput.Native;

namespace WiiTUIO.Provider
{
    public enum WiimoteButton
    {
        Up,
        Down,
        Left,
        Right,
        Home,
        Plus,
        Minus,
        One,
        Two,
        A,
        B
    }

    class WiiKeyMapper
    {

        private string KEYMAPS_PATH = "Keymaps\\";
        private string APPLICATIONS_JSON_FILENAME = "Applications.json";
        private string DEFAULT_JSON_FILENAME = "default.json";

        public Action<WiiButtonEvent> OnButtonDown;
        public Action<WiiButtonEvent> OnButtonUp;

        WiiKeyMap keyMap;
        ButtonState PressedButtons;

        SystemProcessMonitor processMonitor;

        JObject applicationsJson;
        JObject defaultKeymapJson;

        public WiiKeyMapper()
        {
            PressedButtons = new ButtonState();

            System.IO.Directory.CreateDirectory(KEYMAPS_PATH);
            this.applicationsJson = this.createDefaultApplicationsJSON();
            this.defaultKeymapJson = this.createDefaultKeymapJSON();

            this.keyMap = new WiiKeyMap(this.defaultKeymapJson);
            this.keyMap.OnButtonDown += keyMap_onButtonDown;
            this.keyMap.OnButtonUp += keyMap_onButtonUp;

            this.processMonitor = new SystemProcessMonitor();

            this.processMonitor.ProcessChanged += processChanged;
        }

        private void processChanged(ProcessChangedEvent evt)
        {
            try
            {
                string appStringToMatch = evt.Process.MainModule.FileVersionInfo.FileDescription + evt.Process.MainModule.FileVersionInfo.OriginalFilename + evt.Process.MainModule.FileVersionInfo.FileName;

                bool keymapFound = false;

                IEnumerable<JObject> applicationConfigurations = this.applicationsJson.GetValue("Applications").Children<JObject>();
                foreach (JObject configuration in applicationConfigurations)
                {
                    string appName = configuration.GetValue("Name").ToString();

                    if (appStringToMatch.ToLower().Replace(" ", "").Contains(appName.ToLower().Replace(" ", "")))
                    {
                        this.loadKeyMap(KEYMAPS_PATH + configuration.GetValue("Keymap").ToString());
                        keymapFound = true;
                    }
                    
                }
                if (!keymapFound)
                {
                    this.keyMap.jsonObj = this.defaultKeymapJson;
                }

            }
            catch (Exception e)
            {
                Console.WriteLine("Could not change keymap config for " + evt.Process);
            }
        }

        private void keyMap_onButtonUp(WiiButtonEvent evt)
        {
            this.OnButtonUp(evt);
        }

        private void keyMap_onButtonDown(WiiButtonEvent evt)
        {
            this.OnButtonDown(evt);
        }


        private JObject createDefaultApplicationsJSON()
        {
            JArray applications = new JArray();

            JObject applicationList =
                new JObject(
                    new JProperty("Applications",
                        applications),
                    new JProperty("Default", DEFAULT_JSON_FILENAME)
            );

            JObject union = applicationList;

            if (File.Exists(KEYMAPS_PATH + APPLICATIONS_JSON_FILENAME))
            {
                StreamReader reader = File.OpenText(KEYMAPS_PATH + APPLICATIONS_JSON_FILENAME);
                try
                {
                    JObject existingConfig = (JObject)JToken.ReadFrom(new JsonTextReader(reader));
                    reader.Close();

                    MergeJSON(union, existingConfig);
                }
                catch (Exception e) 
                {
                    throw new Exception(KEYMAPS_PATH + APPLICATIONS_JSON_FILENAME + " is not valid JSON");
                }
            }
            
            File.WriteAllText(KEYMAPS_PATH + APPLICATIONS_JSON_FILENAME, union.ToString());
            return union;
        }

        private JObject createDefaultKeymapJSON()
        {
            JObject buttons = new JObject();

            buttons.Add(new JProperty("A", "TouchMaster"));

            buttons.Add(new JProperty("B", "TouchSlave"));

            buttons.Add(new JProperty("Home", "LWin"));

            buttons.Add(new JProperty("Left", "Left"));
            buttons.Add(new JProperty("Right", "Right"));
            buttons.Add(new JProperty("Up", "Up"));
            buttons.Add(new JProperty("Down", "Down"));

            JArray buttonPlus = new JArray();
            buttonPlus.Add(new JValue("LControl"));
            buttonPlus.Add(new JValue("OEM_Plus"));
            buttons.Add(new JProperty("Plus", buttonPlus));

            JArray buttonMinus = new JArray();
            buttonMinus.Add(new JValue("LControl"));
            buttonMinus.Add(new JValue("OEM_Minus"));
            buttons.Add(new JProperty("Minus", buttonMinus));

            buttons.Add(new JProperty("One", "MouseToggle"));

            JObject union = buttons;

            if (File.Exists(KEYMAPS_PATH + DEFAULT_JSON_FILENAME))
            {
                StreamReader reader = File.OpenText(KEYMAPS_PATH + DEFAULT_JSON_FILENAME);
                try
                {
                    JObject existingConfig = (JObject)JToken.ReadFrom(new JsonTextReader(reader));
                    reader.Close();

                    MergeJSON(union, existingConfig);
                }
                catch (Exception e)
                {
                    throw new Exception(KEYMAPS_PATH + DEFAULT_JSON_FILENAME + " is not valid JSON");
                }
            }
            File.WriteAllText(KEYMAPS_PATH + DEFAULT_JSON_FILENAME, union.ToString());
            return union;
        }

        private static void MergeJSON(JObject receiver, JObject donor)
        {
            foreach (var property in donor)
            {
                JObject receiverValue = receiver[property.Key] as JObject;
                JObject donorValue = property.Value as JObject;
                if (receiverValue != null && donorValue != null)
                    MergeJSON(receiverValue, donorValue);
                else
                    receiver[property.Key] = property.Value;
            }
        }

        public void loadKeyMap(string path)
        {

            JObject union = this.defaultKeymapJson;

            if (File.Exists(path))
            {
                StreamReader reader = File.OpenText(path);
                try
                {
                    JObject newKeymap = (JObject)JToken.ReadFrom(new JsonTextReader(reader));
                    reader.Close();

                    MergeJSON(union, newKeymap);
                }
                catch (Exception e)
                {
                    throw new Exception(path + " is not valid JSON");
                }
            }

            this.keyMap.jsonObj = union;

            this.processButtonState(new ButtonState()); //Sets all buttons to "not pressed"

            Console.WriteLine("Loaded new keymap on " + path);
        }

        public void processButtonState(ButtonState buttonState)
        {
            if (buttonState.A && !PressedButtons.A)
            {
                this.keyMap.executeButtonDown(WiimoteButton.A);
                PressedButtons.A = true;
            }
            else if (!buttonState.A && PressedButtons.A)
            {
                this.keyMap.executeButtonUp(WiimoteButton.A);
                PressedButtons.A = false;
            }

            if (buttonState.B && !PressedButtons.B)
            {
                this.keyMap.executeButtonDown(WiimoteButton.B);
                PressedButtons.B = true;
            }
            else if (!buttonState.B && PressedButtons.B)
            {
                this.keyMap.executeButtonUp(WiimoteButton.B);
                PressedButtons.B = false;
            }

            if (buttonState.Up && !PressedButtons.Up)
            {
                this.keyMap.executeButtonDown(WiimoteButton.Up);
                PressedButtons.Up = true;
            }
            else if (!buttonState.Up && PressedButtons.Up)
            {
                this.keyMap.executeButtonUp(WiimoteButton.Up);
                PressedButtons.Up = false;
            }

            if (buttonState.Down && !PressedButtons.Down)
            {
                this.keyMap.executeButtonDown(WiimoteButton.Down);
                PressedButtons.Down = true;
            }
            else if (!buttonState.Down && PressedButtons.Down)
            {
                this.keyMap.executeButtonUp(WiimoteButton.Down);
                PressedButtons.Down = false;
            }

            if (buttonState.Left && !PressedButtons.Left)
            {
                this.keyMap.executeButtonDown(WiimoteButton.Left);
                PressedButtons.Left = true;
            }
            else if (!buttonState.Left && PressedButtons.Left)
            {
                this.keyMap.executeButtonUp(WiimoteButton.Left);
                PressedButtons.Left = false;
            }

            if (buttonState.Right && !PressedButtons.Right)
            {
                this.keyMap.executeButtonDown(WiimoteButton.Right);
                PressedButtons.Right = true;
            }
            else if (!buttonState.Right && PressedButtons.Right)
            {
                this.keyMap.executeButtonUp(WiimoteButton.Right);
                PressedButtons.Right = false;
            }

            if (buttonState.Home && !PressedButtons.Home)
            {
                this.keyMap.executeButtonDown(WiimoteButton.Home);
                PressedButtons.Home = true;
            }
            else if (!buttonState.Home && PressedButtons.Home)
            {
                this.keyMap.executeButtonUp(WiimoteButton.Home);
                PressedButtons.Home = false;
            }

            if (buttonState.Plus && !PressedButtons.Plus)
            {
                this.keyMap.executeButtonDown(WiimoteButton.Plus);
                PressedButtons.Plus = true;
            }
            else if (PressedButtons.Plus && !buttonState.Plus)
            {
                this.keyMap.executeButtonUp(WiimoteButton.Plus);
                PressedButtons.Plus = false;
            }

            if (buttonState.Minus && !PressedButtons.Minus)
            {
                this.keyMap.executeButtonDown(WiimoteButton.Minus);
                PressedButtons.Minus = true;
            }
            else if (PressedButtons.Minus && !buttonState.Minus)
            {
                this.keyMap.executeButtonUp(WiimoteButton.Minus);
                PressedButtons.Minus = false;
            }

            if (buttonState.One && !PressedButtons.One)
            {
                this.keyMap.executeButtonDown(WiimoteButton.One);
                PressedButtons.One = true;
            }
            else if (PressedButtons.One && !buttonState.One)
            {
                this.keyMap.executeButtonUp(WiimoteButton.One);
                PressedButtons.One = false;
            }

            if (buttonState.Two && !PressedButtons.Two)
            {
                this.keyMap.executeButtonDown(WiimoteButton.Two);
                PressedButtons.Two = true;
            }
            else if (PressedButtons.Two && !buttonState.Two)
            {
                this.keyMap.executeButtonUp(WiimoteButton.Two);
                PressedButtons.Two = false;
            }
        }
    }

    public enum MouseCode
    {
        MOUSELEFT,
        MOUSERIGHT
    }

    

    public class WiiKeyMap
    {
        public JObject jsonObj;

        public Action<WiiButtonEvent> OnButtonUp;
        public Action<WiiButtonEvent> OnButtonDown;

        private InputSimulator inputSimulator;

        public WiiKeyMap(JObject jsonObj)
        {
            this.jsonObj = jsonObj;

            this.inputSimulator = new InputSimulator();
        }

        public void executeButtonUp(WiimoteButton button)
        {
            bool handled = false;

            JToken key = this.jsonObj.GetValue(button.ToString()); //ToString converts WiimoteButton.A to "A" for instance

            if (key != null)
            {
                if (Enum.IsDefined(typeof(VirtualKeyCode), key.ToString().ToUpper())) //Enum.Parse does the opposite...
                {
                    this.inputSimulator.Keyboard.KeyUp((VirtualKeyCode)Enum.Parse(typeof(VirtualKeyCode), key.ToString(), true));
                    handled = true;
                }
                else if (Enum.IsDefined(typeof(MouseCode), key.ToString().ToUpper()))
                {
                    MouseCode mouseCode = (MouseCode)Enum.Parse(typeof(MouseCode), key.ToString(), true);
                    switch (mouseCode)
                    {
                        case MouseCode.MOUSELEFT:
                            this.inputSimulator.Mouse.LeftButtonUp();
                            handled = true;
                            break;
                        case MouseCode.MOUSERIGHT:
                            this.inputSimulator.Mouse.RightButtonUp();
                            handled = true;
                            break;
                    }
                }
                else if (key.Values().Count() > 0)
                {
                    IEnumerable<JToken> array = key.Values<JToken>();

                    List<VirtualKeyCode> modifiers = new List<VirtualKeyCode>();

                    for (int i = 0; i < array.Count() - 1; i++)
                    {
                        if (Enum.IsDefined(typeof(VirtualKeyCode), array.ElementAt(i).ToString().ToUpper()))
                        {
                            modifiers.Add((VirtualKeyCode)Enum.Parse(typeof(VirtualKeyCode), array.ElementAt(i).ToString(), true));
                        }
                    }
                    List<VirtualKeyCode> keys = new List<VirtualKeyCode>();
                    if (Enum.IsDefined(typeof(VirtualKeyCode), array.Last().ToString().ToUpper()))
                    {
                        keys.Add((VirtualKeyCode)Enum.Parse(typeof(VirtualKeyCode), array.Last().ToString(), true));
                    }


                    if (modifiers.Count() > 0 && key.Count() > 0)
                    {
                        this.inputSimulator.Keyboard.ModifiedKeyStroke(modifiers, keys);
                        handled = true;
                    }
                }

                OnButtonUp(new WiiButtonEvent(key.ToString(), button, handled));
            }

        }

        public void executeButtonDown(WiimoteButton button)
        {
            bool handled = false;

            JToken key = this.jsonObj.GetValue(button.ToString());
            if (key != null)
            {
                if (Enum.IsDefined(typeof(VirtualKeyCode), key.ToString().ToUpper()))
                {
                    this.inputSimulator.Keyboard.KeyDown((VirtualKeyCode)Enum.Parse(typeof(VirtualKeyCode), key.ToString(), true));
                    handled = true;
                }
                else if (Enum.IsDefined(typeof(MouseCode), key.ToString().ToUpper()))
                {
                    MouseCode mouseCode = (MouseCode)Enum.Parse(typeof(MouseCode), key.ToString(), true);
                    switch (mouseCode)
                    {
                        case MouseCode.MOUSELEFT:
                            this.inputSimulator.Mouse.LeftButtonDown();
                            handled = true;
                            break;
                        case MouseCode.MOUSERIGHT:
                            this.inputSimulator.Mouse.RightButtonDown();
                            handled = true;
                            break;
                    }

                }

                OnButtonDown(new WiiButtonEvent(key.ToString(), button, handled));
            }
        }
    }

    public class WiiButtonEvent
    {
        public bool Handled = false;
        public string Action = "";
        public WiimoteButton Button;

        public WiiButtonEvent(string action, WiimoteButton button, bool handled = false)
        {
            this.Action = action;
            this.Button = button;
            this.Handled = handled;
        }

    }
}