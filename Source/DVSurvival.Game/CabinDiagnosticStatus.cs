namespace DVSurvival.Mod
{
    // Capturing sampled values allocates no strings. Formatting is needed only while
    // UMM requests the diagnostic label; reuse it across its Layout/Repaint calls.
    internal sealed class CabinDiagnosticStatus
    {
        private bool valid, open, dm1u, dieselHeater, heaterOn, fanOn, running;
        private int openings;
        private float outside;
        private string formatted;
        private bool formattedRussian;

        internal void Clear() { valid = false; formatted = null; }

        internal void Capture(bool open, int openings, bool dm1u, bool dieselHeater,
            bool heaterOn, bool fanOn, bool running, float outside)
        {
            this.open = open; this.openings = openings; this.dm1u = dm1u;
            this.dieselHeater = dieselHeater; this.heaterOn = heaterOn;
            this.fanOn = fanOn; this.running = running; this.outside = outside;
            valid = true; formatted = null;
        }

        internal string Text
        {
            get
            {
                if (!valid) return string.Empty;
                var russian = ModLocalization.IsRussian;
                if (formatted != null && formattedRussian == russian) return formatted;
                var controls = dm1u || dieselHeater
                    ? ModLocalization.Text("отопитель ", "heater ") + OnOff(heaterOn) +
                        ModLocalization.Text("; вентилятор ", "; fan ") + OnOff(fanOn) +
                        (dm1u ? string.Empty : ModLocalization.Text("; двигатель ", "; engine ") + OnOff(running))
                    : ModLocalization.Text("двигатель ", "engine ") + OnOff(running);
                formatted = ModLocalization.Text("Кабина: проёмы ", "Cabin: openings ") +
                    (open ? ModLocalization.Text("открыты", "open") : ModLocalization.Text("закрыты", "closed")) +
                    " [" + openings + "]; " + controls +
                    ModLocalization.Text("; улица ", "; outdoors ") + ModLocalization.Number(outside, "F1") + " °C";
                formattedRussian = russian;
                return formatted;
            }
        }

        private static string OnOff(bool value)
        { return value ? ModLocalization.Text("включён", "on") : ModLocalization.Text("выключен", "off"); }
    }
}
