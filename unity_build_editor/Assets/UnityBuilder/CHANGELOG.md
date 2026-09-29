# Changelog

Format theo [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), version theo [SemVer](https://semver.org/).

## [0.0.8] - 2026-09-29
### Added
- Nút "Áp dụng + Save (No Ads / Has Ads)": set No Ads Rule + save scene mà không build, để kiểm tra scene trước khi build.

## [0.0.7] - 2026-09-29
### Changed
- Đổi nhãn ô giá trị No Ads Rule: "Khi No Ads" → **Value No Ads**, "Khi có Ads" → **Value Has Ads** (field config giữ nguyên).

## [0.0.6] - 2026-09-29
### Added
- No Ads Rules riêng từng project (lưu trong `RaccoonBuildConfig.json`): set field bool/int/float/string/enum của GameObject/Component trong scene theo cờ No Ads, save scene khi build; lỗi resolve/parse chặn build.
- UI: rule hiện dưới checkbox No Ads khi bật; chọn scene bằng SceneAsset picker / menu scene trong project; chọn object theo hierarchy scene đã chọn (tự mở tạm scene chưa mở); kéo object để tự điền, dropdown component/property, nút kiểm tra thử (dry run) cho No Ads / có Ads.
- Ô giá trị rule theo kiểu property (dropdown bool/enum, ô số int/float), kiểu lưu lại khi chọn Property bằng ▼.
- No Ads bật được khi chưa cài adpack nếu có rule.

### Changed
- History tách 2 tab riêng: **History APK** / **History AAB** (kèm số lượng).

## [0.0.5] - 2026-09-29
### Fixed
- adb install thêm `-d` (cho phép cài bản version code thấp hơn nếu bản cũ debuggable).
- Lỗi `INSTALL_FAILED_VERSION_DOWNGRADE` / `INSTALL_FAILED_UPDATE_INCOMPATIBLE` → hỏi uninstall bản cũ rồi cài lại + launch.

## [0.0.4] - 2026-09-29
### Changed
- Build luôn nén LZ4HC (`BuildOptions.CompressWithLz4HC`) cho cả Dev và Release.

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
