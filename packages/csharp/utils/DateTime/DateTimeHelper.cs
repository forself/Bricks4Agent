using System;
using System.Globalization;

namespace Bricks4Agent.Utils.DateTime
{
    /// <summary>
    /// DateTime utility methods and extension
    /// </summary>
    public static class DateTimeHelper
    {
        #region Constants

        /// <summary>
        /// Unix epoch (1970-01-01 00:00:00 UTC)
        /// </summary>
        public static readonly System.DateTime UnixEpoch = new System.DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        #endregion

        #region Unix Timestamp

        /// <summary>
        /// Convert DateTime to Unix timestamp (seconds)
        /// </summary>
        public static long ToUnixTimestamp(this System.DateTime dateTime)
        {
            return (long)(dateTime.ToUniversalTime() - UnixEpoch).TotalSeconds;
        }

        /// <summary>
        /// Convert DateTime to Unix timestamp (milliseconds)
        /// </summary>
        public static long ToUnixTimestampMs(this System.DateTime dateTime)
        {
            return (long)(dateTime.ToUniversalTime() - UnixEpoch).TotalMilliseconds;
        }

        /// <summary>
        /// Convert Unix timestamp (seconds) to DateTime
        /// </summary>
        public static System.DateTime FromUnixTimestamp(long timestamp)
        {
            return UnixEpoch.AddSeconds(timestamp);
        }

        /// <summary>
        /// Convert Unix timestamp (milliseconds) to DateTime
        /// </summary>
        public static System.DateTime FromUnixTimestampMs(long timestamp)
        {
            return UnixEpoch.AddMilliseconds(timestamp);
        }

        #endregion

        #region ISO 8601

        /// <summary>
        /// Convert DateTime to ISO 8601 string
        /// </summary>
        public static string ToIso8601(this System.DateTime dateTime)
        {
            return dateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        }

        /// <summary>
        /// Parse ISO 8601 string to DateTime
        /// </summary>
        public static System.DateTime FromIso8601(string iso8601)
        {
            return System.DateTime.Parse(iso8601, null, DateTimeStyles.RoundtripKind);
        }

        /// <summary>
        /// Try parse ISO 8601 string to DateTime
        /// </summary>
        public static bool TryParseIso8601(string iso8601, out System.DateTime result)
        {
            return System.DateTime.TryParse(iso8601, null, DateTimeStyles.RoundtripKind, out result);
        }

        #endregion

        #region Formatting

        /// <summary>
        /// Format as "2026-01-23"
        /// </summary>
        public static string ToShortDate(this System.DateTime dateTime)
        {
            return dateTime.ToString("yyyy-MM-dd");
        }

        /// <summary>
        /// Format as "2026-01-23 15:30:45"
        /// </summary>
        public static string ToLongDate(this System.DateTime dateTime)
        {
            return dateTime.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// Format as "January 23, 2026"
        /// </summary>
        public static string ToReadableDate(this System.DateTime dateTime)
        {
            return dateTime.ToString("MMMM dd, yyyy");
        }

        /// <summary>
        /// Format as "Jan 23, 2026 3:30 PM"
        /// </summary>
        public static string ToReadableDateTime(this System.DateTime dateTime)
        {
            return dateTime.ToString("MMM dd, yyyy h:mm tt");
        }

        #endregion

        #region Relative Time

        /// <summary>
        /// Get relative time string (e.g., "2 hours ago", "in 3 days")
        /// </summary>
        public static string ToRelativeTime(this System.DateTime dateTime)
        {
            return ToRelativeTime(dateTime, System.DateTime.UtcNow);
        }

        /// <summary>
        /// Get relative time string from specific reference time
        /// </summary>
        public static string ToRelativeTime(this System.DateTime dateTime, System.DateTime referenceTime)
        {
            var timeSpan = referenceTime - dateTime;
            var isPast = timeSpan.TotalSeconds > 0;

            var seconds = Math.Abs(timeSpan.TotalSeconds);
            var minutes = Math.Abs(timeSpan.TotalMinutes);
            var hours = Math.Abs(timeSpan.TotalHours);
            var days = Math.Abs(timeSpan.TotalDays);

            string result;

            if (seconds < 60)
            {
                result = "just now";
                return result;
            }
            else if (minutes < 60)
            {
                var count = (int)minutes;
                result = $"{count} minute{(count != 1 ? "s" : "")}";
            }
            else if (hours < 24)
            {
                var count = (int)hours;
                result = $"{count} hour{(count != 1 ? "s" : "")}";
            }
            else if (days < 7)
            {
                var count = (int)days;
                result = $"{count} day{(count != 1 ? "s" : "")}";
            }
            else if (days < 30)
            {
                var count = (int)(days / 7);
                result = $"{count} week{(count != 1 ? "s" : "")}";
            }
            else if (days < 365)
            {
                var count = (int)(days / 30);
                result = $"{count} month{(count != 1 ? "s" : "")}";
            }
            else
            {
                var count = (int)(days / 365);
                result = $"{count} year{(count != 1 ? "s" : "")}";
            }

            return isPast ? $"{result} ago" : $"in {result}";
        }

        #endregion

        #region Start/End of Period

        /// <summary>
        /// Get start of day (00:00:00)
        /// </summary>
        public static System.DateTime StartOfDay(this System.DateTime dateTime)
        {
            return dateTime.Date;
        }

        /// <summary>
        /// Get end of day (23:59:59.999)
        /// </summary>
        public static System.DateTime EndOfDay(this System.DateTime dateTime)
        {
            return dateTime.Date.AddDays(1).AddMilliseconds(-1);
        }

        /// <summary>
        /// Get start of week (Monday)
        /// </summary>
        public static System.DateTime StartOfWeek(this System.DateTime dateTime, DayOfWeek firstDayOfWeek = DayOfWeek.Monday)
        {
            var diff = (7 + (dateTime.DayOfWeek - firstDayOfWeek)) % 7;
            return dateTime.AddDays(-diff).Date;
        }

        /// <summary>
        /// Get end of week (Sunday)
        /// </summary>
        public static System.DateTime EndOfWeek(this System.DateTime dateTime, DayOfWeek firstDayOfWeek = DayOfWeek.Monday)
        {
            return dateTime.StartOfWeek(firstDayOfWeek).AddDays(7).AddMilliseconds(-1);
        }

        /// <summary>
        /// Get start of month
        /// </summary>
        public static System.DateTime StartOfMonth(this System.DateTime dateTime)
        {
            return new System.DateTime(dateTime.Year, dateTime.Month, 1);
        }

        /// <summary>
        /// Get end of month
        /// </summary>
        public static System.DateTime EndOfMonth(this System.DateTime dateTime)
        {
            return dateTime.StartOfMonth().AddMonths(1).AddMilliseconds(-1);
        }

        /// <summary>
        /// Get start of year
        /// </summary>
        public static System.DateTime StartOfYear(this System.DateTime dateTime)
        {
            return new System.DateTime(dateTime.Year, 1, 1);
        }

        /// <summary>
        /// Get end of year
        /// </summary>
        public static System.DateTime EndOfYear(this System.DateTime dateTime)
        {
            return new System.DateTime(dateTime.Year, 12, 31, 23, 59, 59, 999);
        }

        #endregion

        #region Age Calculation

        /// <summary>
        /// Calculate age in years
        /// </summary>
        public static int GetAge(this System.DateTime birthDate)
        {
            return GetAge(birthDate, System.DateTime.Today);
        }

        /// <summary>
        /// Calculate age at specific date
        /// </summary>
        public static int GetAge(this System.DateTime birthDate, System.DateTime atDate)
        {
            var age = atDate.Year - birthDate.Year;

            if (atDate.Month < birthDate.Month ||
                (atDate.Month == birthDate.Month && atDate.Day < birthDate.Day))
            {
                age--;
            }

            return age;
        }

        #endregion

        #region Business Days

        /// <summary>
        /// Check if date is a weekend
        /// </summary>
        public static bool IsWeekend(this System.DateTime dateTime)
        {
            return dateTime.DayOfWeek == DayOfWeek.Saturday || dateTime.DayOfWeek == DayOfWeek.Sunday;
        }

        /// <summary>
        /// Check if date is a weekday
        /// </summary>
        public static bool IsWeekday(this System.DateTime dateTime)
        {
            return !dateTime.IsWeekend();
        }

        /// <summary>
        /// Add business days (skip weekends)
        /// </summary>
        public static System.DateTime AddBusinessDays(this System.DateTime dateTime, int days)
        {
            var result = dateTime;
            var increment = days > 0 ? 1 : -1;
            var remaining = Math.Abs(days);

            while (remaining > 0)
            {
                result = result.AddDays(increment);

                if (result.IsWeekday())
                {
                    remaining--;
                }
            }

            return result;
        }

        /// <summary>
        /// Calculate business days between two dates
        /// </summary>
        public static int GetBusinessDays(System.DateTime startDate, System.DateTime endDate)
        {
            var days = 0;
            var current = startDate.Date;
            var end = endDate.Date;

            while (current < end)
            {
                if (current.IsWeekday())
                {
                    days++;
                }
                current = current.AddDays(1);
            }

            return days;
        }

        #endregion

        #region Comparison

        /// <summary>
        /// Check if date is today
        /// </summary>
        public static bool IsToday(this System.DateTime dateTime)
        {
            return dateTime.Date == System.DateTime.Today;
        }

        /// <summary>
        /// Check if date is yesterday
        /// </summary>
        public static bool IsYesterday(this System.DateTime dateTime)
        {
            return dateTime.Date == System.DateTime.Today.AddDays(-1);
        }

        /// <summary>
        /// Check if date is tomorrow
        /// </summary>
        public static bool IsTomorrow(this System.DateTime dateTime)
        {
            return dateTime.Date == System.DateTime.Today.AddDays(1);
        }

        /// <summary>
        /// Check if date is in the past
        /// </summary>
        public static bool IsPast(this System.DateTime dateTime)
        {
            return dateTime < System.DateTime.UtcNow;
        }

        /// <summary>
        /// Check if date is in the future
        /// </summary>
        public static bool IsFuture(this System.DateTime dateTime)
        {
            return dateTime > System.DateTime.UtcNow;
        }

        /// <summary>
        /// Check if dates are on the same day
        /// </summary>
        public static bool IsSameDay(this System.DateTime date1, System.DateTime date2)
        {
            return date1.Date == date2.Date;
        }

        #endregion

        #region Range

        /// <summary>
        /// Check if date is between two dates (inclusive)
        /// </summary>
        public static bool IsBetween(this System.DateTime dateTime, System.DateTime start, System.DateTime end)
        {
            return dateTime >= start && dateTime <= end;
        }

        /// <summary>
        /// Clamp date to range
        /// </summary>
        public static System.DateTime Clamp(this System.DateTime dateTime, System.DateTime min, System.DateTime max)
        {
            if (dateTime < min) return min;
            if (dateTime > max) return max;
            return dateTime;
        }

        #endregion

        #region Quarter

        /// <summary>
        /// Get quarter (1-4)
        /// </summary>
        public static int GetQuarter(this System.DateTime dateTime)
        {
            return (dateTime.Month - 1) / 3 + 1;
        }

        /// <summary>
        /// Get start of quarter
        /// </summary>
        public static System.DateTime StartOfQuarter(this System.DateTime dateTime)
        {
            var quarter = dateTime.GetQuarter();
            var month = (quarter - 1) * 3 + 1;
            return new System.DateTime(dateTime.Year, month, 1);
        }

        /// <summary>
        /// Get end of quarter
        /// </summary>
        public static System.DateTime EndOfQuarter(this System.DateTime dateTime)
        {
            return dateTime.StartOfQuarter().AddMonths(3).AddMilliseconds(-1);
        }

        #endregion

        #region Truncate

        /// <summary>
        /// Truncate to seconds (remove milliseconds)
        /// </summary>
        public static System.DateTime TruncateToSeconds(this System.DateTime dateTime)
        {
            return new System.DateTime(dateTime.Year, dateTime.Month, dateTime.Day,
                dateTime.Hour, dateTime.Minute, dateTime.Second, dateTime.Kind);
        }

        /// <summary>
        /// Truncate to minutes (remove seconds and milliseconds)
        /// </summary>
        public static System.DateTime TruncateToMinutes(this System.DateTime dateTime)
        {
            return new System.DateTime(dateTime.Year, dateTime.Month, dateTime.Day,
                dateTime.Hour, dateTime.Minute, 0, dateTime.Kind);
        }

        /// <summary>
        /// Truncate to hours
        /// </summary>
        public static System.DateTime TruncateToHours(this System.DateTime dateTime)
        {
            return new System.DateTime(dateTime.Year, dateTime.Month, dateTime.Day,
                dateTime.Hour, 0, 0, dateTime.Kind);
        }

        #endregion

        #region Time Zone

        /// <summary>
        /// Convert to specific time zone
        /// </summary>
        public static System.DateTime ToTimeZone(this System.DateTime dateTime, string timeZoneId)
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return TimeZoneInfo.ConvertTime(dateTime, timeZone);
        }

        /// <summary>
        /// Convert to UTC
        /// </summary>
        public static System.DateTime ToUtc(this System.DateTime dateTime)
        {
            if (dateTime.Kind == DateTimeKind.Utc)
                return dateTime;

            return TimeZoneInfo.ConvertTimeToUtc(dateTime);
        }

        #endregion
    }
}
