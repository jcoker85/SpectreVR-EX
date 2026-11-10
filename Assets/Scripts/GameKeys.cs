using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace SpectreEX
{
    // One thing the player can do, and the up to three keys or mouse buttons and three gamepad inputs that do it
    public class GameKey
    {
        public readonly string label;
        public readonly int[] slots = new int[GameKeys.Slots];
        public readonly int[] pads = new int[GameKeys.Slots];
        internal readonly int[] defaults;
        internal int[] padDefaults = new int[0];

        internal GameKey(string label, params int[] keys)
        {
            this.label = label;
            defaults = keys;
            keys.CopyTo(slots, 0);
        }

        internal GameKey Pad(params int[] buttons)
        {
            padDefaults = buttons;
            buttons.CopyTo(pads, 0);
            return this;
        }

        public bool Held
        {
            get
            {
                foreach (int code in slots)
                {
                    var button = GameKeys.Button(code);
                    if (button != null && button.isPressed) return true;
                }
                foreach (int code in pads)
                {
                    var button = GameKeys.PadButton(code);
                    if (button != null && button.ReadValue() > PadPress) return true;
                }
                return false;
            }
        }

        public bool Pressed
        {
            get
            {
                foreach (int code in slots)
                {
                    var button = GameKeys.Button(code);
                    if (button != null && button.wasPressedThisFrame) return true;
                }
                foreach (int code in pads)
                {
                    var button = GameKeys.PadButton(code);
                    if (button != null && button.wasPressedThisFrame) return true;
                }
                return false;
            }
        }

        const float PadPress = 0.3f;   // the same point PlayerMotor reads a drive input at
    }

    // The keyboard, mouse and gamepad controls, set on the Controls page behind Shift + Options. The
    // keyboard defaults are the owner's layout, keeping the original's numpad keys. Esc and the menus' own
    // keys stay fixed.
    public static class GameKeys
    {
        public const int Slots = 3;
        const int MouseBase = 1000;   // codes from here up are mouse buttons, below are the Key enum
        const int MouseLeft = MouseBase, MouseRight = MouseBase + 1;
        static readonly string[] MouseNames = { "MouseLeft", "MouseRight", "MouseMiddle", "MouseForward", "MouseBack" };

        // Gamepad inputs, by their number in this list
        static readonly string[] PadNames =
        {
            "", "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "LS Up", "LS Down", "LS Left", "LS Right",
            "RS Up", "RS Down", "RS Left", "RS Right", "LS Click", "RS Click", "DP Up", "DP Down", "DP Left", "DP Right",
            "Start", "Select",
        };
        const int PadA = 1, PadB = 2, PadX = 3, PadY = 4, PadLB = 5, PadRB = 6, PadRT = 8,
            PadLSUp = 9, PadLSDown = 10, PadLSLeft = 11, PadLSRight = 12, PadStart = 23, PadSelect = 24;

        static GameKey New(string label, params Key[] keys) => new GameKey(label, Array.ConvertAll(keys, k => (int)k));

        // The keyboard defaults are the owner's layout (2026-11-09): WASD with QEZC for the diagonals,
        // numpad as the original's, F to jump, R / T to look aside, V for the next view
        public static readonly GameKey
            Forward = New("FORWARD", Key.W, Key.Numpad8).Pad(PadLSUp),
            Back = New("BACK", Key.S, Key.Numpad2).Pad(PadLSDown),
            TurnLeft = New("TURN LEFT", Key.A, Key.Numpad4).Pad(PadLSLeft),
            TurnRight = New("TURN RIGHT", Key.D, Key.Numpad6).Pad(PadLSRight),
            ForwardLeft = New("FORWARD LEFT", Key.Q, Key.Numpad7),
            ForwardRight = New("FORWARD RIGHT", Key.E, Key.Numpad9),
            BackLeft = New("BACK LEFT", Key.Z, Key.Numpad1),
            BackRight = New("BACK RIGHT", Key.C, Key.Numpad3),
            Fire = new GameKey("FIRE", (int)Key.Space, (int)Key.Numpad0, MouseLeft).Pad(PadRT, PadA),
            HeavyWeapon = New("HEAVY WEAPON", Key.Tab).Pad(PadX),
            NextWeapon = New("NEXT WEAPON", Key.LeftCtrl, Key.RightCtrl).Pad(PadLB, PadRB),
            Hyperspace = New("HYPERSPACE", Key.LeftAlt).Pad(PadY),
            Jump = new GameKey("JUMP", (int)Key.F, (int)Key.Numpad5, MouseRight).Pad(PadB),
            LookLeft = New("LOOK LEFT", Key.R),
            LookRight = New("LOOK RIGHT", Key.T),
            NextView = New("NEXT VIEW", Key.V, Key.NumpadMultiply),
            ZoomIn = New("ZOOM IN", Key.Equals, Key.NumpadPlus),
            ZoomOut = New("ZOOM OUT", Key.Minus, Key.NumpadMinus),
            Pause = New("PAUSE", Key.P).Pad(PadStart),
            // Esc's job, ending the game the original's way; Esc itself stays fixed, so this is for a pad
            QuitGame = New("QUIT GAME").Pad(PadSelect),
            Sound = New("SOUND", Key.F2),
            FloorDots = New("FLOOR DOTS", Key.F3),
            Wire = New("WIRE", Key.F4),
            Textures = New("TEXTURES", Key.F5),
            Movies = New("MOVIES", Key.F6),
            Music = New("MUSIC", Key.M);

        public static readonly GameKey[] Weapons = MakeWeapons();

        static GameKey[] MakeWeapons()
        {
            var keys = new GameKey[8];
            for (int i = 0; i < keys.Length; i++) keys[i] = New($"WEAPON {i + 1}", Key.Digit1 + i);
            return keys;
        }

        public static readonly List<GameKey> All = MakeAll();

        static List<GameKey> MakeAll()
        {
            var all = new List<GameKey> { Forward, Back, TurnLeft, TurnRight, ForwardLeft, ForwardRight, BackLeft, BackRight, Fire, HeavyWeapon, NextWeapon };
            all.AddRange(Weapons);
            all.AddRange(new[] { Hyperspace, Jump, LookLeft, LookRight, NextView, ZoomIn, ZoomOut, Pause, QuitGame, Sound, FloorDots, Wire, Textures, Movies, Music });
            return all;
        }

        public static ButtonControl Button(int code)
        {
            if (code == 0) return null;
            if (code < MouseBase) return Keyboard.current?[(Key)code];
            var m = Mouse.current;
            if (m == null) return null;
            switch (code - MouseBase)
            {
                case 0: return m.leftButton;
                case 1: return m.rightButton;
                case 2: return m.middleButton;
                case 3: return m.forwardButton;
                default: return m.backButton;
            }
        }

        // A bound input, while the Options dialog's Joystick box is ticked
        public static ButtonControl PadButton(int code) => GameSettings.Joystick ? PadControl(Gamepad.current, code) : null;

        static ButtonControl PadControl(Gamepad g, int code)
        {
            if (g == null) return null;
            switch (code)
            {
                case 1: return g.buttonSouth;
                case 2: return g.buttonEast;
                case 3: return g.buttonWest;
                case 4: return g.buttonNorth;
                case 5: return g.leftShoulder;
                case 6: return g.rightShoulder;
                case 7: return g.leftTrigger;
                case 8: return g.rightTrigger;
                case 9: return g.leftStick.up;
                case 10: return g.leftStick.down;
                case 11: return g.leftStick.left;
                case 12: return g.leftStick.right;
                case 13: return g.rightStick.up;
                case 14: return g.rightStick.down;
                case 15: return g.rightStick.left;
                case 16: return g.rightStick.right;
                case 17: return g.leftStickButton;
                case 18: return g.rightStickButton;
                case 19: return g.dpad.up;
                case 20: return g.dpad.down;
                case 21: return g.dpad.left;
                case 22: return g.dpad.right;
                case 23: return g.startButton;
                case 24: return g.selectButton;
                default: return null;
            }
        }

        public static string PadName(int code) => PadNames[code];

        // Whatever gamepad input went down this frame, or 0
        public static int JustPressedPad()
        {
            for (int code = 1; code < PadNames.Length; code++)
                if (PadControl(Gamepad.current, code)?.wasPressedThisFrame == true) return code;
            return 0;
        }

        // Whatever key or mouse button went down this frame, or 0
        public static int JustPressed()
        {
            var k = Keyboard.current;
            if (k != null)
                foreach (var key in k.allKeys)
                    if (key != null && key.wasPressedThisFrame) return (int)key.keyCode;
            for (int i = 0; i < MouseNames.Length; i++)
                if (Button(MouseBase + i)?.wasPressedThisFrame == true) return MouseBase + i;
            return 0;
        }

        // A key does one thing: taking it for this action frees it from any other
        public static void Bind(GameKey action, int slot, int code, bool pad)
        {
            foreach (var other in All)
            {
                var slots = pad ? other.pads : other.slots;
                for (int i = 0; i < Slots; i++)
                    if (slots[i] == code) slots[i] = 0;
            }
            (pad ? action.pads : action.slots)[slot] = code;
            Save();
        }

        public static void Clear(GameKey action, int slot, bool pad)
        {
            (pad ? action.pads : action.slots)[slot] = 0;
            Save();
        }

        public static void Reset(bool pad)
        {
            foreach (var action in All)
            {
                var slots = pad ? action.pads : action.slots;
                Array.Clear(slots, 0, Slots);
                (pad ? action.padDefaults : action.defaults).CopyTo(slots, 0);
            }
            Save();
        }

        static string Serial(int code) =>
            code == 0 ? "" : code >= MouseBase ? MouseNames[code - MouseBase] : ((Key)code).ToString();

        static int Parse(string name)
        {
            int mouse = Array.IndexOf(MouseNames, name);
            if (mouse >= 0) return MouseBase + mouse;
            return Enum.TryParse(name, out Key key) ? (int)key : 0;
        }

        static string Pref(GameKey action) => "key." + action.label;

        static string PadPref(GameKey action) => "pad." + action.label;

        static void Save()
        {
            foreach (var action in All)
            {
                PlayerPrefs.SetString(Pref(action), string.Join(",", Array.ConvertAll(action.slots, Serial)));
                PlayerPrefs.SetString(PadPref(action), string.Join(",", Array.ConvertAll(action.pads, PadName)));
            }
            PlayerPrefs.Save();
        }

        public static void Load()
        {
            foreach (var action in All)
            {
                if (PlayerPrefs.HasKey(Pref(action)))
                {
                    var names = PlayerPrefs.GetString(Pref(action)).Split(',');
                    for (int i = 0; i < Slots; i++) action.slots[i] = i < names.Length ? Parse(names[i]) : 0;
                }
                if (PlayerPrefs.HasKey(PadPref(action)))
                {
                    var names = PlayerPrefs.GetString(PadPref(action)).Split(',');
                    for (int i = 0; i < Slots; i++) action.pads[i] = i < names.Length ? Mathf.Max(0, Array.IndexOf(PadNames, names[i])) : 0;
                }
            }
        }

        // Short enough for a slot on the Controls page
        public static string Name(int code)
        {
            if (code == 0) return "";
            if (code >= MouseBase) return "MOUSE" + (code - MouseBase + 1);
            var key = (Key)code;
            switch (key)
            {
                case Key.Backspace: return "BKSP";
                case Key.Backquote: return "TILDE";
                case Key.LeftShift: return "LSHIFT";
                case Key.RightShift: return "RSHIFT";
                case Key.LeftAlt: return "LALT";
                case Key.RightAlt: return "RALT";
                case Key.LeftCtrl: return "LCTRL";
                case Key.RightCtrl: return "RCTRL";
                case Key.PageUp: return "PGUP";
                case Key.PageDown: return "PGDN";
                case Key.Insert: return "INS";
                case Key.Delete: return "DEL";
                case Key.Equals: return "=";
                case Key.Minus: return "-";
                case Key.NumpadMultiply: return "NUM*";
                case Key.NumpadPlus: return "NUM+";
                case Key.NumpadMinus: return "NUM-";
                case Key.NumpadDivide: return "NUM/";
                case Key.NumpadPeriod: return "NUM.";
                case Key.NumpadEquals: return "NUM=";
                case Key.NumpadEnter: return "NUMENT";
            }
            string name = key.ToString().Replace("Arrow", "").Replace("Numpad", "NUM").Replace("Digit", "").ToUpperInvariant();
            return name.Length > 10 ? name.Substring(0, 10) : name;
        }

        // The first key set for an action, for on-screen prompts
        public static string First(GameKey action)
        {
            foreach (int code in action.slots)
                if (code != 0) return Name(code);
            return "UNSET";
        }
    }
}
