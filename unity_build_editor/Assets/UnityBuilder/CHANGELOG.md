# Changelog

Format theo [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), version theo [SemVer](https://semver.org/).

## [0.0.3] - 2026-09-29
### Changed
- Ô kéo thả Icon to hơn (96×96) và luôn vuông (không bị indent của foldout làm méo).

## [0.0.2] - 2026-09-29
### Added
- Mục App: sửa App Name (`PlayerSettings.productName`) và Icon (kéo thả texture).
- Kéo icon → set Default Icon + clear mọi icon Android Adaptive/Round/Legacy để tránh override cũ gây lỗi; nút "Clear icon Android" khi còn sót.

## [0.0.1] - 2026-09-29
### Added
- EditorWindow `Tools/Raccoon/Android Build`.
- Config chung `ProjectSettings/RaccoonBuildConfig.json`: version, version code, keystore, output folder, scene list.
- Build preset (Dev → APK/ARM64, Release → AAB/ARMv7+ARM64, luôn IL2CPP), Development Build, No Ads / Use Test Ad, clean cache, auto-increment version code, template tên file.
- Validate trước build: scene, App ID, module Android/NDK, keystore (Release), switch platform, AdMob mediation.
- Tích hợp `com.raccoon.adpack` (không reference assembly): set `_creativeMode` / `use_test_ad` + save scene/asset.
- Review read-only: Define Symbols, AdMob mediation (UPM + Dependencies.xml ↔ mainTemplate.gradle, Force Resolve EDM4U).
- Build history `Library/RaccoonBuildHistory.json` (20 APK / 10 AAB), adb device list / install / launch.
- EditMode tests: build rules, tên file, version code, ads theo Mode, validator, parser mediation, history.
