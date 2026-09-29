using CarScanner.Models;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace CarScanner.Services.OBD
{
    /// <summary>
    /// Decodes raw ELM327 Mode 03 (stored), Mode 07 (pending), and Mode 0A (permanent)
    /// Diagnostic Trouble Code responses into structured DiagnosticCode objects, per
    /// SAE J2012. Purely computational — no I/O, no Bluetooth, no async.
    /// </summary>
    public sealed class DTCDecoder
    {
        private const byte ModeStoredResponsePrefix = 0x43; // Mode 03 positive response
        private const byte ModePendingResponsePrefix = 0x47; // Mode 07 positive response
        private const byte ModePermanentResponsePrefix = 0x4A; // Mode 0A positive response

        private static readonly Dictionary<string, string> CodeDescriptions = new(StringComparer.OrdinalIgnoreCase)
        {
            ["P0100"] = "Mass or Volume Air Flow Circuit Malfunction",
            ["P0101"] = "Mass or Volume Air Flow Circuit Range/Performance Problem",
            ["P0102"] = "Mass or Volume Air Flow Circuit Low Input",
            ["P0103"] = "Mass or Volume Air Flow Circuit High Input",
            ["P0110"] = "Intake Air Temperature Circuit Malfunction",
            ["P0115"] = "Engine Coolant Temperature Circuit Malfunction",
            ["P0120"] = "Throttle/Pedal Position Sensor/Switch A Circuit Malfunction",
            ["P0130"] = "O2 Sensor Circuit Malfunction (Bank 1, Sensor 1)",
            ["P0133"] = "O2 Sensor Circuit Slow Response (Bank 1, Sensor 1)",
            ["P0141"] = "O2 Sensor Heater Circuit Malfunction (Bank 1, Sensor 2)",
            ["P0171"] = "System Too Lean (Bank 1)",
            ["P0172"] = "System Too Rich (Bank 1)",
            ["P0200"] = "Injector Circuit Malfunction",
            ["P0300"] = "Random/Multiple Cylinder Misfire Detected",
            ["P0301"] = "Cylinder 1 Misfire Detected",
            ["P0302"] = "Cylinder 2 Misfire Detected",
            ["P0303"] = "Cylinder 3 Misfire Detected",
            ["P0304"] = "Cylinder 4 Misfire Detected",
            ["P0325"] = "Knock Sensor 1 Circuit Malfunction",
            ["P0335"] = "Crankshaft Position Sensor A Circuit Malfunction",
            ["P0340"] = "Camshaft Position Sensor A Circuit Malfunction",
            ["P0400"] = "Exhaust Gas Recirculation Flow Malfunction",
            ["P0401"] = "Exhaust Gas Recirculation Flow Insufficient Detected",
            ["P0402"] = "Exhaust Gas Recirculation Flow Excessive Detected",
            ["P0420"] = "Catalyst System Efficiency Below Threshold (Bank 1)",
            ["P0430"] = "Catalyst System Efficiency Below Threshold (Bank 2)",
            ["P0440"] = "Evaporative Emission Control System Malfunction",
            ["P0442"] = "Evaporative Emission Control System Leak Detected (small leak)",
            ["P0446"] = "Evaporative Emission Control System Vent Control Circuit Malfunction",
            ["P0455"] = "Evaporative Emission Control System Leak Detected (large leak)",
            ["P0500"] = "Vehicle Speed Sensor Malfunction",
            ["P0505"] = "Idle Control System Malfunction",
            ["P0520"] = "Engine Oil Pressure Sensor/Switch Circuit Malfunction",
            ["P0562"] = "System Voltage Low",
            ["P0606"] = "ECM/PCM Processor Malfunction",
            ["P0700"] = "Transmission Control System Malfunction",
            ["P0705"] = "Transmission Range Sensor Circuit Malfunction",
            ["P0715"] = "Input/Turbine Speed Sensor Circuit Malfunction",
            ["P0720"] = "Output Speed Sensor Circuit Malfunction",
            ["P0740"] = "Torque Converter Clutch Circuit Malfunction",
            ["P0750"] = "Shift Solenoid A Malfunction",
            ["P0755"] = "Shift Solenoid B Malfunction",
            ["P0760"] = "Shift Solenoid C Malfunction",
            ["P0770"] = "Shift Solenoid D Malfunction",
            ["P0780"] = "Shift Malfunction",
            ["P0800"] = "Transfer Case Control System Malfunction",
            ["P1101"] = "Mass Air Flow Sensor Out of Self-Test Range",
            ["P2101"] = "Throttle Actuator Control Motor Circuit Range/Performance",
            ["P2135"] = "Throttle/Pedal Position Sensor/Switch A/B Voltage Correlation",
            ["P2195"] = "O2 Sensor Signal Biased/Stuck Lean (Bank 1, Sensor 1)",
            ["P2196"] = "O2 Sensor Signal Biased/Stuck Rich (Bank 1, Sensor 1)",
            ["P2227"] = "Barometric Pressure Circuit Range/Performance",
            ["P2228"] = "Barometric Pressure Circuit Low Input",
            ["P2261"] = "Turbo/Super Charger Bypass Valve Control Circuit Range/Performance",
            ["P2262"] = "Turbo/Super Charger Boost Pressure Not Detected",
            ["P2270"] = "O2 Sensor Signal Stuck Lean (Bank 1, Sensor 2)",
            ["P2271"] = "O2 Sensor Signal Stuck Rich (Bank 1, Sensor 2)",
            ["P2272"] = "O2 Sensor Signal Stuck Lean (Bank 2, Sensor 2)",
            ["P2273"] = "O2 Sensor Signal Stuck Rich (Bank 2, Sensor 2)",
            ["P2274"] = "O2 Sensor Signal Circuit Low Voltage (Bank 1, Sensor 2)",
            ["P2275"] = "O2 Sensor Signal Circuit High Voltage (Bank 1, Sensor 2)",
            ["P2276"] = "O2 Sensor Signal Circuit Low Voltage (Bank 2, Sensor 2)",
            ["P2277"] = "O2 Sensor Signal Circuit High Voltage (Bank 2, Sensor 2)",
            ["P2278"] = "O2 Sensor Signal Circuit Low Voltage (Bank 1, Sensor 3)",
            ["P2279"] = "O2 Sensor Signal Circuit High Voltage (Bank 1, Sensor 3)",
            ["P2280"] = "O2 Sensor Signal Circuit Low Voltage (Bank 2, Sensor 3)",
            ["P2281"] = "O2 Sensor Signal Circuit High Voltage (Bank 2, Sensor 3)",
            ["P2282"] = "Air Leak Between Throttle Body and Intake Valves",
            ["P2283"] = "Injector Control Pressure Circuit/Open",
            ["P2284"] = "Injector Control Pressure Sensor Circuit Range/Performance",
            ["P2285"] = "Injector Control Pressure Sensor Circuit Low",
            ["P2286"] = "Injector Control Pressure Sensor Circuit High",
            ["P2287"] = "Injector Control Pressure Too High",
            ["P2288"] = "Injector Control Pressure Too High - Engine Off",
            ["P2289"] = "Injector Control Pressure Too Low",
            ["P2290"] = "Injector Control Pressure Too Low - Engine Off",
            ["P2291"] = "Injector Control Pressure Erratic",
            ["P2292"] = "Fuel Pump Flow/Performance",
            ["P2293"] = "Fuel Pressure Regulator 2 Performance"
        };

        private const string UnknownCodeDescription = "Unknown Diagnostic Trouble Code";

        #region Public API

        /// <summary>Decodes a Mode 03 (stored) DTC response.</summary>
        public List<DiagnosticCode> DecodeStored(string rawResponse)
        {
            return DecodeMultiple(rawResponse, ModeStoredResponsePrefix, DiagnosticCodeStatus.Stored);
        }

        /// <summary>Decodes a Mode 07 (pending) DTC response.</summary>
        public List<DiagnosticCode> DecodePending(string rawResponse)
        {
            return DecodeMultiple(rawResponse, ModePendingResponsePrefix, DiagnosticCodeStatus.Pending);
        }

        /// <summary>Decodes a Mode 0A (permanent) DTC response.</summary>
        public List<DiagnosticCode> DecodePermanent(string rawResponse)
        {
            return DecodeMultiple(rawResponse, ModePermanentResponsePrefix, DiagnosticCodeStatus.Permanent);
        }

        /// <summary>
        /// Decodes a single two-byte DTC pair into a structured DiagnosticCode.
        /// The first byte's top two bits select the category (P/C/B/U) and the
        /// remaining six bits combine with the second byte to form the numeric suffix,
        /// per SAE J2012.
        /// </summary>
        public DiagnosticCode DecodeSingle(byte firstByte, byte secondByte, DiagnosticCodeStatus status)
        {
            var categoryBits = (firstByte & 0xC0) >> 6;
            var category = MapCategory(categoryBits);
            var categoryChar = MapCategoryChar(categoryBits);

            var firstDigit = (firstByte & 0x30) >> 4;
            var secondDigit = firstByte & 0x0F;
            var thirdDigit = (secondByte & 0xF0) >> 4;
            var fourthDigit = secondByte & 0x0F;

            var code = string.Format(
                CultureInfo.InvariantCulture,
                "{0}{1}{2}{3}{4}",
                categoryChar,
                firstDigit,
                secondDigit.ToString("X1", CultureInfo.InvariantCulture),
                thirdDigit.ToString("X1", CultureInfo.InvariantCulture),
                fourthDigit.ToString("X1", CultureInfo.InvariantCulture));

            var description = CodeDescriptions.TryGetValue(code, out var knownDescription)
                ? knownDescription
                : UnknownCodeDescription;

            return new DiagnosticCode(code, description, category, status);
        }

        #endregion

        #region Internal Parsing

        private List<DiagnosticCode> DecodeMultiple(string rawResponse, byte expectedPrefix, DiagnosticCodeStatus status)
        {
            var bytes = ParseHexBytes(rawResponse);
            var prefixIndex = -1;
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == expectedPrefix) { prefixIndex = i; break; }
            }
            if (prefixIndex < 0)
            {
                if (rawResponse.Contains("NO DATA", StringComparison.OrdinalIgnoreCase)) return new List<DiagnosticCode>();
                throw new FormatException($"DTC პასუხში {expectedPrefix:X2} header ვერ მოიძებნა: '{rawResponse}'.");
            }

            var codes = new List<DiagnosticCode>();
            for (var i = prefixIndex + 1; i + 1 < bytes.Length; i += 2)
            {
                var first = bytes[i];
                var second = bytes[i + 1];
                if (first == 0x00 && second == 0x00) continue;
                codes.Add(DecodeSingle(first, second, status));
            }
            return codes;
        }

        private static byte[] ParseHexBytes(string rawResponse)
        {
            var bytes = PIDDecoder.ParseHexBytes(rawResponse);
            if (bytes.Length == 0 && !string.IsNullOrWhiteSpace(rawResponse))
                throw new FormatException($"DTC პასუხი hex მონაცემებს არ შეიცავს: '{rawResponse}'.");
            return bytes;
        }

        private static DiagnosticCodeCategory MapCategory(int categoryBits)
        {
            return categoryBits switch
            {
                0 => DiagnosticCodeCategory.Powertrain,
                1 => DiagnosticCodeCategory.Chassis,
                2 => DiagnosticCodeCategory.Body,
                3 => DiagnosticCodeCategory.Network,
                _ => throw new FormatException($"Invalid DTC category bits: {categoryBits}.")
            };
        }

        private static char MapCategoryChar(int categoryBits)
        {
            return categoryBits switch
            {
                0 => 'P',
                1 => 'C',
                2 => 'B',
                3 => 'U',
                _ => throw new FormatException($"Invalid DTC category bits: {categoryBits}.")
            };
        }

        #endregion
    }
}