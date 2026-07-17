#pragma once

#include <cstdint>

struct Version
{
    union
    {
        struct { uint16_t major, minor, build, revision; };
        uint64_t version;
    };

    constexpr Version() : version{ 0 } {}
    constexpr Version(uint64_t value) : version{ value } {}
    constexpr Version(uint16_t majorValue, uint16_t minorValue,
        uint16_t buildValue = 0, uint16_t revisionValue = 0)
        : major{ majorValue }, minor{ minorValue }, build{ buildValue }, revision{ revisionValue } {}
};

constexpr Version PLUGIN_SDK_VERSION{ 3, 1, 0, 0 };

struct PluginInfo
{
    Version sdk_version{ PLUGIN_SDK_VERSION };
    bool hide_from_peb;
    bool erase_pe_header;
    bool(__cdecl* init)(Version version);
    void(__cdecl* oep_notify)(Version version);
    int priority;
    const wchar_t* target_apps;
};
