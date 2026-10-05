# Third-party notices (app)

## OBDb — vehicle signal sets

The hybrid / EV battery request layouts (byte positions and scaling) in
`Services/ToyotaHybridBattery.cs`, `Services/HyundaiKiaBattery.cs`,
`Services/GmVoltBoltBattery.cs`, `Services/NissanLeafBattery.cs` and
`Services/MoreEvBatteries.cs`, `Services/MoreEvBatteries2.cs`, and the
recorded car responses used in `Tests/CarScanner.Tests/ToyotaHybridBatteryTests.cs`
`HyundaiKiaBatteryTests.cs`, `GmVoltBoltBatteryTests.cs`, `NissanLeafBatteryTests.cs` and `MoreEvBatteriesTests.cs`, are adapted from **OBDb**
(https://github.com/OBDb — Toyota-Prius, Toyota-RAV4-Hybrid,
Toyota-Camry-Hybrid, Toyota-Corolla, Toyota-C-HR, Kia-Niro,
Hyundai-Kona-Electric, Hyundai-IONIQ-Electric, Hyundai-Santa-Fe-Hybrid,
Chevrolet-Volt, Chevrolet-Bolt-EV, Chevrolet-Bolt-EUV, Nissan-Leaf, BMW-i3, Ford-Fusion-Hybrid, Ford-Mustang-Mach-E, Volkswagen-e-Golf, Audi-e-tron,
Haval-Jolion-HEV, Honda-CR-V-Hybrid, Honda-Civic, Jaguar-I-PACE, MG-ZS-EV, Porsche-Taycan and
related repositories), licensed under the Creative Commons Attribution-ShareAlike 4.0 International
licence (https://creativecommons.org/licenses/by-sa/4.0/).

Changes: the definitions were re-expressed as C# decoders with plausibility
checks; the assessment limits and scoring are GCar360's own.

## PriusChat community

The 7E3 variant of the 2010–2015 block request (21 81, value × 79.99 / 65535)
follows the community-shared Torque PID list on PriusChat. The app tries the
OBDb-documented 7E2 address first and marks the 7E3 variant as not yet verified.
