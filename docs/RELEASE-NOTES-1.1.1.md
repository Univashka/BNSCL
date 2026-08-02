# BNSCL 1.1.1

## Русский

- BNSCL больше не перезаписывает существующий `Win64\winmm.dll` и не создаёт его резервную копию.
- Если `winmm.dll` отсутствует, LoaderU скачивается по HTTPS и устанавливается только после проверки размера и SHA-256.
- `bnscleaner.dll` по-прежнему устанавливается в `Win64\LoaderU`.
- Исправление исключает переустановку LoaderU, который уже был установлен RUMETR.

Полностью закройте Blade & Soul NEO перед установкой плагина.

## English

- BNSCL no longer overwrites or backs up an existing `Win64\winmm.dll`.
- When `winmm.dll` is missing, LoaderU is downloaded over HTTPS and installed only after size and SHA-256 verification.
- `bnscleaner.dll` is still installed into `Win64\LoaderU`.
- This prevents BNSCL from reinstalling LoaderU already installed by RUMETR.

Close Blade & Soul NEO completely before installing the plugin.
