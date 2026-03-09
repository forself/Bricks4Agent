# File Handling Module

A comprehensive file upload system with configurable path templates, upload rules, and batch upload support.

## Features

- **Single and Batch Upload**: Support for uploading single files or multiple files at once
- **Configurable Path Templates**: Dynamic path generation using template variables
- **Upload Rules**: Customizable rules for file validation (extensions, size limits)
- **File Tracking**: Database records for all uploaded files with metadata
- **Soft Delete**: Safe deletion with option to preserve or remove physical files
- **Security**: Built-in validation against path traversal and dangerous file types

## Directory Structure

```
FileHandling/
├── Controllers/
│   ├── FileUploadController.cs    # File upload API endpoints
│   └── UploadRuleController.cs    # Rule management endpoints
├── Services/
│   ├── IFileUploadService.cs      # Service interface
│   ├── FileUploadService.cs       # Service implementation
│   └── PathTemplateParser.cs      # Path template parsing
├── Models/
│   ├── FileUploadRequest.cs       # Single upload request
│   ├── BatchUploadRequest.cs      # Batch upload request
│   ├── FileUploadResult.cs        # Upload result DTOs
│   ├── FileRecord.cs              # File record entity
│   └── UploadPathRule.cs          # Upload rule entity
└── README.md
```

## Setup

### 1. Register Services

Add the following to your `Startup.cs` or `Program.cs`:

```csharp
// Register services
services.AddScoped<IPathTemplateParser, PathTemplateParser>();
services.AddScoped<IFileUploadService, FileUploadService>();
services.AddScoped<IGenericRepository<FileRecord>, GenericRepository<FileRecord>>();
services.AddScoped<IGenericRepository<UploadPathRule>, GenericRepository<UploadPathRule>>();
```

### 2. Configuration

Add to `appsettings.json`:

```json
{
  "FileUpload": {
    "PathRoot": "uploads"
  }
}
```

### 3. Database Migration

Create entity configurations for `FileRecord` and `UploadPathRule`:

```csharp
public class FileRecordConfiguration : IEntityTypeConfiguration<FileRecord>
{
    public void Configure(EntityTypeBuilder<FileRecord> builder)
    {
        builder.ToTable("FileRecords");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OriginalName).IsRequired().HasMaxLength(255);
        builder.Property(x => x.StoredPath).IsRequired().HasMaxLength(500);
        builder.HasIndex(x => new { x.TableName, x.TablePk });
        builder.HasIndex(x => x.Identify);
    }
}

public class UploadPathRuleConfiguration : IEntityTypeConfiguration<UploadPathRule>
{
    public void Configure(EntityTypeBuilder<UploadPathRule> builder)
    {
        builder.ToTable("UploadPathRules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RuleName).IsRequired().HasMaxLength(100);
        builder.Property(x => x.PathTemplate).IsRequired().HasMaxLength(500);
        builder.HasIndex(x => x.RuleName).IsUnique();
    }
}
```

## API Endpoints

### File Upload

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/files/upload` | Upload a single file |
| POST | `/api/files/upload-batch` | Upload multiple files |
| GET | `/api/files` | Query file records |
| GET | `/api/files/{id}` | Get file by ID |
| DELETE | `/api/files/{id}` | Delete a file |
| GET | `/api/files/my-files` | Get current user's files |

### Upload Rules (Admin)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/upload-rules` | List all rules |
| GET | `/api/upload-rules/{id}` | Get rule by ID |
| POST | `/api/upload-rules` | Create new rule |
| PUT | `/api/upload-rules/{id}` | Update rule |
| DELETE | `/api/upload-rules/{id}` | Delete rule |
| PATCH | `/api/upload-rules/{id}/toggle-active` | Toggle rule status |
| POST | `/api/upload-rules/validate-template` | Validate path template |
| GET | `/api/upload-rules/template-variables` | List template variables |

## Path Template Variables

The path template system supports the following variables:

| Variable | Description | Example |
|----------|-------------|---------|
| `{PathRoot}` | Root directory from config | `uploads` |
| `{TableName}` | Associated table name | `Products` |
| `{TablePk}` | Table primary key | `123` |
| `{Identify}` | Custom identifier | `documents` |
| `{FileName}` | Original file name | `image.jpg` |
| `{FileNameWithoutExt}` | File name without extension | `image` |
| `{FileExt}` | File extension with dot | `.jpg` |
| `{UserId}` | Current user ID | `42` |
| `{Date:format}` | Date with format | `{Date:yyyy/MM/dd}` -> `2024/01/15` |
| `{GUID}` | Unique identifier | `a1b2c3d4e5f6...` |

### Example Templates

```
# Basic template
{PathRoot}/{Date:yyyy/MM/dd}/{GUID}_{FileName}
-> uploads/2024/01/15/abc123_photo.jpg

# Organized by table
{PathRoot}/{TableName}/{TablePk}/{Date:yyyyMMdd}_{FileName}
-> uploads/Products/42/20240115_product-image.jpg

# User-specific
{PathRoot}/users/{UserId}/documents/{Identify}/{GUID}{FileExt}
-> uploads/users/5/documents/invoices/xyz789.pdf
```

## Usage Examples

### Upload a Single File

```http
POST /api/files/upload
Content-Type: multipart/form-data
Authorization: Bearer <token>

file: (binary)
ruleId: 1
tableName: Products
tablePk: 42
```

**Response:**
```json
{
  "success": true,
  "statusCode": 201,
  "message": "File uploaded successfully",
  "data": {
    "success": true,
    "fileId": 123,
    "originalName": "product-image.jpg",
    "storedPath": "uploads/Products/42/20240115_abc123_product-image.jpg",
    "fileSize": 245678,
    "contentType": "image/jpeg",
    "uploadedAt": "2024-01-15T10:30:00Z"
  }
}
```

### Batch Upload

```http
POST /api/files/upload-batch
Content-Type: multipart/form-data
Authorization: Bearer <token>

files: (binary)
files: (binary)
files: (binary)
ruleId: 1
tableName: Products
tablePk: 42
```

**Response:**
```json
{
  "success": true,
  "message": "3/3 files uploaded successfully",
  "data": {
    "totalFiles": 3,
    "successCount": 3,
    "failedCount": 0,
    "allSuccessful": true,
    "results": [
      { "success": true, "fileId": 124, "originalName": "image1.jpg", ... },
      { "success": true, "fileId": 125, "originalName": "image2.jpg", ... },
      { "success": true, "fileId": 126, "originalName": "image3.jpg", ... }
    ]
  }
}
```

### Create Upload Rule

```http
POST /api/upload-rules
Content-Type: application/json
Authorization: Bearer <admin-token>

{
  "ruleName": "Product Images",
  "pathTemplate": "{PathRoot}/products/{TablePk}/images/{Date:yyyyMMdd}_{GUID}{FileExt}",
  "description": "Upload rule for product images",
  "allowedExtensions": ".jpg,.jpeg,.png,.webp",
  "maxFileSize": 5242880,
  "isActive": true
}
```

### Query Files

```http
GET /api/files?tableName=Products&tablePk=42
Authorization: Bearer <token>
```

**Response:**
```json
{
  "success": true,
  "data": [
    {
      "id": 123,
      "originalName": "product-image.jpg",
      "storedPath": "uploads/Products/42/...",
      "fileSize": 245678,
      "contentType": "image/jpeg",
      "tableName": "Products",
      "tablePk": "42",
      "uploadedBy": 5,
      "uploadedAt": "2024-01-15T10:30:00Z"
    }
  ]
}
```

## Security Considerations

1. **File Extension Validation**: Configure allowed extensions per rule
2. **File Size Limits**: Set maximum file size per rule (default: 10MB)
3. **Dangerous Files**: Automatic blocking of executable files (.exe, .dll, .bat, etc.)
4. **Path Traversal**: Prevention of `..` patterns in paths
5. **Authentication**: All endpoints require authentication
6. **Authorization**: Rule management requires Admin role

## Frontend Integration

Use with the `BatchUploader` JavaScript component:

```javascript
import { BatchUploader } from './ui_components/form/BatchUploader';

const uploader = new BatchUploader({
    container: '#upload-container',
    apiEndpoint: '/api/files/upload',
    ruleId: 1,
    tableName: 'Products',
    tablePk: '42',
    maxFiles: 10,
    headers: {
        'Authorization': 'Bearer ' + token
    },
    onComplete: (result) => {
        console.log('Upload complete:', result);
    }
});
```

See the frontend component documentation for more details.
