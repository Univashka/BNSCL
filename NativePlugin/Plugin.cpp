#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <Psapi.h>
#include <sddl.h>
#include <atomic>
#include <cstdio>
#include <string>
#include <thread>
#include "pluginsdk.h"

#pragma comment(lib, "psapi.lib")
#pragma comment(lib, "advapi32.lib")

namespace
{
    constexpr wchar_t kPipeName[] = L"\\\\.\\pipe\\BNSCLCleaner";
    std::atomic<bool> g_started{ false };
    std::atomic<bool> g_running{ false };

    SIZE_T WorkingSet()
    {
        PROCESS_MEMORY_COUNTERS_EX counters{};
        counters.cb = sizeof(counters);
        if (!GetProcessMemoryInfo(GetCurrentProcess(),
            reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&counters), sizeof(counters)))
            return 0;
        return counters.WorkingSetSize;
    }

    std::string CleanMemory()
    {
        const SIZE_T before = WorkingSet();
        SetLastError(ERROR_SUCCESS);
        const BOOL trimmed = SetProcessWorkingSetSize(
            GetCurrentProcess(), static_cast<SIZE_T>(-1), static_cast<SIZE_T>(-1));
        const DWORD error = trimmed ? ERROR_SUCCESS : GetLastError();
        const SIZE_T after = WorkingSet();

        char response[160]{};
        std::snprintf(response, sizeof(response), "%s|%llu|%llu|%lu\n",
            trimmed ? "OK" : "ERROR",
            static_cast<unsigned long long>(before),
            static_cast<unsigned long long>(after),
            static_cast<unsigned long>(error));
        return response;
    }

    void ServeClient(HANDLE pipe)
    {
        char buffer[256]{};
        DWORD read = 0;
        if (!ReadFile(pipe, buffer, sizeof(buffer) - 1, &read, nullptr) || read == 0) return;

        std::string command(buffer, buffer + read);
        std::string response = command.find("clean") != std::string::npos
            ? CleanMemory()
            : "ERROR|0|0|87\n";
        DWORD written = 0;
        WriteFile(pipe, response.data(), static_cast<DWORD>(response.size()), &written, nullptr);
        FlushFileBuffers(pipe);
    }

    void Worker()
    {
        g_running.store(true);
        PSECURITY_DESCRIPTOR descriptor = nullptr;
        SECURITY_ATTRIBUTES security{};
        security.nLength = sizeof(security);
        if (ConvertStringSecurityDescriptorToSecurityDescriptorW(
            L"D:(A;;GA;;;AU)(A;;GA;;;SY)(A;;GA;;;BA)",
            SDDL_REVISION_1, &descriptor, nullptr))
        {
            security.lpSecurityDescriptor = descriptor;
        }

        while (g_running.load())
        {
            HANDLE pipe = CreateNamedPipeW(
                kPipeName,
                PIPE_ACCESS_DUPLEX,
                PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
                1, 512, 512, 0, descriptor ? &security : nullptr);
            if (pipe == INVALID_HANDLE_VALUE)
            {
                Sleep(500);
                continue;
            }

            const BOOL connected = ConnectNamedPipe(pipe, nullptr)
                ? TRUE
                : GetLastError() == ERROR_PIPE_CONNECTED;
            if (connected && g_running.load()) ServeClient(pipe);
            DisconnectNamedPipe(pipe);
            CloseHandle(pipe);
        }
        if (descriptor) LocalFree(descriptor);
    }

    void StartOnce()
    {
        if (g_started.exchange(true)) return;
        std::thread(Worker).detach();
    }

    bool __cdecl PluginInit(Version) { StartOnce(); return true; }
    void __cdecl PluginOep(Version) { StartOnce(); }
    static const wchar_t kTargets[] = L"Client.exe\0BNSR.exe\0";
}

extern "C" __declspec(dllexport) PluginInfo GPluginInfo{
    PLUGIN_SDK_VERSION, false, false, &PluginInit, &PluginOep, 0, kTargets
};

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(module);
        StartOnce();
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        g_running.store(false);
    }
    return TRUE;
}
