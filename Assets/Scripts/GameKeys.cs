using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace SpectreEX
{
    // One thing the player can do, and the up to four keys or mouse buttons that do it
    public class GameKey
    {
        public readonly string label;
        public readonly int[] slots = new int[GameKeys.Slots];
        internal readonly int[] defaults;

        internal GameKey(string label, params int[] keys)
        {
            this.label = label;
            defaults = keys;
            keys.CopyTo(slots, 0);
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
                return false;
            }
        }
    }

    // The keyboard and mouse controls, set on the Controls page behind Shift + Options. The defaults are
    // the original's keys and the modern ones together. Esc, the menus' own keys and the gamepad stay fixed.
    public static class GameKeys
    {
        public const int Slots = 4;
        const int MouseBase = 1000;   // codes from here up are mouse buttons, below are the Key enum
        const int MouseLeft = MouseBase, MouseRight = MouseBase + 1;
        static readonly string[] MouseNames = { "MouseLeft", "MouseRight", "MouseMiddle", "MouseForward", "MouseBack" };

        static GameKey New(string label, params Key[] keys) => new GameKey(label, Array.ConvertAll(keys, k => (int)k));

        public static readonly GameKey
            Forward = New("FORWARD", Key.W, Key.UpArrow, Key.Numpad8),
            Back = New("BACK", Key.S, Key.DownArrow, Key.Numpad2),
            TurnLeft = New("TURN LEFT", Key.A, Key.LeftArrow, Key.Numpad4),
            TurnRight = New("TURN RIGHT", Key.D, Key.RightArrow, Key.Numpad6),
            ForwardLeft = New("FORWARD LEFT", Key.Numpad7, Key.Home),
            ForwardRight = New("FORWARD RIGHT", Key.Numpad9, Key.PageUp),
            BackLeft = New("BACK LEFT", Key.Numpad1, Key.End),
            BackRight = New("BACK RIGHT", Key.Numpad3, Key.PageDown),
            Fire = new GameKey("FIRE", (int)Key.Space, (int)Key.Numpad0, (int)Key.Insert, MouseLeft),
            HeavyWeapon = New("HEAVY WEAPON", Key.LeftAlt, Key.RightAlt),
            NextWeapon = New("NEXT WEAPON", Key.LeftCtrl, Key.RightCtrl),
            Hyperspace = New("HYPERSPACE", Key.Backspace, Key.Backquote),
            Jump = new GameKey("JUMP", (int)Key.J, (int)Key.Numpad5, MouseRight),
            LookLeft = New("LOOK LEFT", Key.LeftShift),
            LookRight = New("LOOK RIGHT", Key.RightShift),
            NextView = New("NEXT VIEW", Key.Tab, Key.NumpadMultiply),
            ZoomIn = New("ZOOM IN", Key.Equals, Key.NumpadPlus),
            ZoomOut = New("ZOOM OUT", Key.Minus, Key.NumpadMinus),
            Pause = New("PAUSE", Key.P),
            Sound = New("SOUND", Key.F2),
            FloorDots = New("FLOOR DOTS", Key.F3),
            Wire = New("WIRE", Key.F),
            Textures = New("TEXTURES", Key.F5),
            Movies = New("MOVIES", Key.F6),
            Music = New("MUSIC", Key.M),
            NextTrack = New("NEXT TRACK", Key.N);

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
            all.AddRange(new[] { Hyperspace, Jump, LookLeft, LookRight, NextView, ZoomIn, ZoomOut, Pause, Sound, FloorDots, Wire, Textures, Movies, Music, NextTrack });
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
        public static void Bind(GameKey action, int slot, int code)
        {
            foreach (var other in All)
                for (int i = 0; i < Slots; i++)
                    if (other.slots[i] == code) other.slots[i] = 0;
            action.slots[slot] = code;
            Save();
        }

        public static void Clear(GameKey action, int slot)
        {
            action.slots[slot] = 0;
            Save();
        }

        public static void Reset()
        {
            foreach (var action in All)
            {
                Array.Clear(action.slots, 0, Slots);
                action.defaults.CopyTo(action.slots, 0);
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

        static void Save()
        {
            foreach (var action in All) PlayerPrefs.SetString(Pref(action), string.Join(",", Array.ConvertAll(action.slots, Serial)));
            PlayerPrefs.Save();
        }

        public static void Load()
        {
            foreach (var action in All)
            {
                if (!PlayerPrefs.HasKey(Pref(action))) continue;
                var names = PlayerPrefs.GetString(Pref(action)).Split(',');
                for (int i = 0; i < Slots; i++) action.slots[i] = i < names.Length ? Parse(names[i]) : 0;
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
            return name.Length > 7 ? name.Substring(0, 7) : name;
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
