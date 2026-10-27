using System;
using System.Collections.Generic;

namespace LevelLens.Runtime.Network
{
    [Serializable]
    public class ErrorResponseDto
    {
        public string error;
    }

    [Serializable]
    public class LoginRequestDto
    {
        public string email;
        public string password;
    }

    [Serializable]
    public class AuthUserDto
    {
        public string id;
        public string email;
        public string role;
    }

    [Serializable]
    public class LoginResponseDto
    {
        public string token;
        public AuthUserDto user;
    }

    [Serializable]
    public class ValidateTokenResponseDto
    {
        public string email;
    }

    [Serializable]
    public class ReportAttachmentDto
    {
        public string r2Key;
        public string fileName;
        public string mediaType;           // image | video
        public int sizeBytes;
        public float videoClampStart;      // default: 0f
        public float? videoClampEnd;       // null if full video
    }

    [Serializable]
    public class ReportItemDto
    {
        public string itemType;            // bug | feedback
        public string title;               // Max 120 chars
        public string description;         // Max 2,000 chars
        public string priority;            // low | medium | high | blocker (null for feedback)
        public string category;            // improve | art | level_design | good (null for bug)
        public List<ReportAttachmentDto> attachments = new();
    }

    [Serializable]
    public class SubmitReportDto
    {
        public string reportId;             // Guid.NewGuid().ToString()
        public string gameId;               // save-em-all
        public string gameName;             // Save 'Em All
        public string versionName;          // Application.version
        public string holderName;           // adapter.CurrentLevelHolderName
        public string levelName;            // adapter.CurrentLevelId
        public int levelIndex;              // adapter.CurrentLevelIndex
        public string testerName;           // Form input
        public int score;                   // 1-10
        public string difficultyTag;        // Easy | Medium | Hard | Very Hard
        public string screenshotKey;        // Presigned R2 key
        public int reviveCount;             // Session telemetry
        public int failCount;               // Session telemetry
        public int restartCount;            // Session telemetry
        public int playtimeSeconds;         // Session telemetry
        public List<ReportItemDto> items;   // Max 10 items
    }

    [Serializable]
    public class PresignRequestDto
    {
        public string fileName;
        public string mediaType;            // image | video
        public string mimeType;             // image/jpeg | image/png | video/mp4
        public int sizeBytes;
    }

    [Serializable]
    public class PresignResponseDto
    {
        public string r2Key;
        public string uploadUrl;
    }
}
