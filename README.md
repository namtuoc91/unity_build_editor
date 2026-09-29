# unity_build_editor

Repo dev UPM package `com.raccoon.build-editor` (Unity 6). Source package: `unity_build_editor/Assets/UnityBuilder/`.

## Import vào project khác
Package Manager → `+` → **Add package from git URL...**

```
https://github.com/namtuoc91/unity_build_editor.git#v0.0.1   # pin version (khuyên dùng)
https://github.com/namtuoc91/unity_build_editor.git#upm      # luôn bản mới nhất
```

## Release (GitHub Action `UPM Release`)
1. Bump `version` trong `unity_build_editor/Assets/UnityBuilder/package.json` + ghi `CHANGELOG.md`.
2. Commit đầy đủ file `.meta` (mở Unity cho nó sinh meta trước khi commit).
3. Push lên `main` → action tự chạy:
   - kiểm tra SemVer + đủ `.meta`,
   - tạo commit từ thư mục package (bỏ `Tests/`) → push branch `upm`,
   - tạo tag `v<version>` (tag đã có thì bỏ qua, chỉ cập nhật `upm`).
4. Chạy tay: tab **Actions → UPM Release → Run workflow** (`force_retag` = tạo lại tag đã có).

> Không commit trực tiếp vào branch `upm` — branch này được sinh tự động.
