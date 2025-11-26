[<-- Back to main README.md](README.md)

# Publishing with Updaemon

This guide explains how to prepare your application for distribution via Updaemon, including how to use the `updaemon.json` configuration file to help Updaemon locate your executable.

To clarify, usage of the `updaemon.json` file is optional. Updaemon works for releases without it too, but if the file structure of your release is complicated so that it's hard for Updaemon to find what file is the actual executable, then using `updaemon.json` to specify which file to run could be useful.

## Overview

When Updaemon downloads and installs your application, it:
1. Downloads the release file (typically a zip archive) to a versioned directory (e.g., `/opt/my-app/1.0.0/`)
2. Extracts zip files automatically
3. Unwraps single top-level directories (if the zip contains only one directory)
4. Searches for the executable file to run

## The `updaemon.json` File

You can include an `updaemon.json` file in your published output to explicitly tell Updaemon where to find your executable. This is especially useful when:
- Your executable name doesn't match the service name
- Your executable is in a subdirectory
- Your zip file structure is complex

### File Location

Place `updaemon.json` at the **root of your published output** (the top-level directory that will be extracted from the zip file into the version directory). This is the same location regardless of whether the directory structure gets unwrapped or not.

**Example 1: Zip with single directory (will be unwrapped)**

If your zip structure is:
```
publish.zip
└── publish/
    ├── my-app
    ├── updaemon.json
    └── other-files...
```

After extraction and unwrapping, `updaemon.json` will be at the version directory root, and your `executablePath` should reference files relative to that root:

```json
{
  "executablePath": "my-app"
}
```

**Example 2: Zip with multiple top-level items (won't be unwrapped)**

If your zip structure is:
```
publish.zip
├── publish/
│   └── my-app
├── config/
│   └── config.json
└── updaemon.json
```

After extraction, `updaemon.json` remains at the version directory root, and your `executablePath` should reference files relative to that root (e.g., `"publish/my-app"`).

```json
{
  "executablePath": "publish/my-app"
}
```

### Configuration Format

```json
{
  "executablePath": "path/to/your-executable"
}
```

The `executablePath` is a **relative path** from the version directory root to your executable file.

## Path Resolution

### How Updaemon Processes Downloads

1. **Download**: The release file is downloaded to the version directory (e.g., `/opt/my-app/1.0.0/`)
2. **Extraction**: If it's a zip file, it's automatically extracted into the same directory
3. **Unwrapping**: If the zip contains only a single top-level directory, its contents are moved up one level and the directory is removed
4. **Detection**: Updaemon looks for `updaemon.json` at the root of the version directory

### Example Scenarios

#### Scenario 1: Simple Structure (Unwrapped)

If your zip contains:
```
publish/
├── EasyReasy.KnowledgeBase.Web.Server
├── appsettings.json
└── other-files...
```

After extraction and unwrapping, the structure becomes:
```
/opt/my-app/1.0.0/
├── EasyReasy.KnowledgeBase.Web.Server
├── appsettings.json
├── other-files...
└── updaemon.json
```

Your `updaemon.json` should be:
```json
{
  "executablePath": "EasyReasy.KnowledgeBase.Web.Server"
}
```

#### Scenario 2: Complex Structure (Not Unwrapped)

If your zip contains multiple top-level items:
```
publish/
├── EasyReasy.KnowledgeBase.Web.Server
└── other-files...
config/
└── config.json
```

After extraction (no unwrapping), the structure is:
```
/opt/my-app/1.0.0/
├── publish/
│   ├── EasyReasy.KnowledgeBase.Web.Server
│   └── other-files...
├── config/
│   └── config.json
└── updaemon.json
```

Your `updaemon.json` should be:
```json
{
  "executablePath": "publish/EasyReasy.KnowledgeBase.Web.Server"
}
```

#### Scenario 3: Executable in Subdirectory

If your executable is in a subdirectory:
```
bin/
└── my-app
lib/
└── dependencies...
```

Your `updaemon.json` should be:
```json
{
  "executablePath": "bin/my-app"
}
```

## Fallback Behavior

If `updaemon.json` is not present, Updaemon will:
1. First try to find an executable with an exact name match to the service name
2. Then try a partial name match
3. Search recursively through all subdirectories

However, using `updaemon.json` is recommended for clarity and reliability.

## Best Practices

1. **Always include `updaemon.json`** when your executable name differs from the service name
2. **Use relative paths** - the path is always relative to the version directory root
3. **Test your structure** - verify that after extraction and unwrapping, the path in `updaemon.json` correctly points to your executable
4. **Place the file at the root** - `updaemon.json` should be somewhere that ends up at the root after unwrapping

## Example: Complete Publishing Workflow

1. Build your application and publish it to a `publish` directory
2. Create `updaemon.json` in the `publish` directory with the correct `executablePath`
3. Zip the `publish` directory contents (not the directory itself)
4. Upload the zip file to your distribution source (GitHub releases, etc.)
5. Updaemon will download, extract, unwrap, and use your `updaemon.json` to find the executable

