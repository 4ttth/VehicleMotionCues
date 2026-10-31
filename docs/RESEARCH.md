# Research notes: vehicle motion cues on laptops

## What Apple shipped

iOS 18 / iPadOS 18 added **Vehicle Motion Cues** (Settings › Accessibility › Motion). Using the
device's accelerometer and gyroscope, it detects when you're in a moving vehicle and animates dots
along the screen edges that shift with braking, acceleration and turns. It can be always on or
automatic, the dot size and colour are adjustable, and there's a Control Center toggle. Apple says
it is not for drivers and works best when the passenger faces forward.

## Why it helps

Motion sickness is mostly a **sensory conflict**: the inner ear feels the car accelerate and turn,
while the eyes are locked on a screen that doesn't move. Moving cues in **peripheral vision** give
the visual system motion that matches what the body feels.

- Prevalence estimates range from ~60% to over two-thirds of people at some point. Children,
  teenagers and back-seat passengers looking down at devices are hit hardest.
- Ford found that adult passengers focused on a screen started feeling sick after about 10 minutes.
- In a 2024 test-track study (Diels et al.), single cues alone weren't statistically significant,
  but **combined peripheral + anticipatory cues cut carsickness by 28%**.
- University of Glasgow work (Pöhlmann et al., CHI 2023; Qiu, PhD thesis 2025) found visual cues
  the most effective mitigation. Cues driven by **acceleration** (not velocity) reduce sickness
  while distracting less.
- de Winkel et al. (Applied Ergonomics, 2021): peripheral and anticipatory visual cues
  significantly reduced motion sickness.
- Caveat: evidence for the dot-style UI specifically is still mostly anecdotal. It's a
  mitigation, not a cure.

## Why a laptop version matters

A laptop on your lap fills more of your vision than a phone and pins your gaze down at the
keyboard, so the conflict is arguably worse. Neither Windows nor macOS offers anything like Vehicle
Motion Cues. The audience is commuters, students and remote workers on buses, trains, ride-shares
and flights, plus people who currently can't use a screen in a vehicle at all.

## The hardware problem and how this app solves it

Most clamshell laptops **have no accelerometer**. On the development machine (Lenovo 83K0),
`Windows.Devices.Sensors.Accelerometer.GetDefault()` returns null. Convertibles and Surface devices
usually do have one, and many Apple-silicon MacBooks have an undocumented IMU behind the sensor
processing unit.

So the app supports two motion sources:

1. **iPhone as the sensor (primary).** Native iOS apps need a Mac, Xcode and an Apple developer
   account, so the phone side is a web page instead. Safari only exposes `DeviceMotionEvent` to
   **secure (HTTPS) pages** after a **user tap + permission prompt**, so the laptop serves the page
   over HTTPS with a self-signed certificate. The user accepts Safari's warning once, and the page
   streams motion over a WebSocket, falling back to batched HTTPS POSTs. This also works over the
   iPhone's **Personal Hotspot** (172.20.10.0/28), which suits cars without Wi-Fi.
2. **The laptop's own accelerometer**, when Windows exposes one.

## Sources

- [9to5Mac — iOS 18 Vehicle Motion Cues](https://9to5mac.com/2024/10/11/ios-18-fixed-my-motion-sickness-problem-with-one-new-feature/)
- [Macworld — How to use Vehicle Motion Cues](https://www.macworld.com/article/2405722/ios-18-how-to-vehicle-motion-cues-reduce-motion-sickness.html)
- [Gear Patrol — Can Apple's new iPhone feature really reduce motion sickness?](https://www.gearpatrol.com/?p=832330)
- [WardsAuto — Ford on the front lines of the fight against car sickness](https://wardsauto.com/technology/ford-front-lines-fight-against-car-sickness)
- [WhichCar — Ford looks at ways to reduce car sickness](https://www.whichcar.com.au/car-advice/ford-looks-at-ways-to-reduce-car-sickness-in-future-cars)
- [Diels et al. 2024 (CMS conferences)](https://openaccess-api.cms-conferences.org/articles/download/978-1-964867-75-5_94)
- [Pöhlmann et al., University of Glasgow](https://eprints.gla.ac.uk/291787)
- [Qiu 2025, Towards non-disruptive visual motion cues (Glasgow thesis)](https://theses.gla.ac.uk/85518)
- [de Winkel et al. — Efficacy of augmented visual environments (TNO)](https://publications.tno.nl/publication/34637359/PRaw0I/winkel-2020-efficacy.pdf)
- [Xie et al. — display placement and motion sickness (IJHCI)](https://personal.hkust-gz.edu.cn/hedengbo/assets/publicationPDFs/Xie_IJHCI_2026b.pdf)
- [Lean Rada — I made an app to fix my motion sickness](https://leanrada.com/notes/motion-sickness-app/)
