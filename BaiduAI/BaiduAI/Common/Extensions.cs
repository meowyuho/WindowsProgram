using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BaiduAI.Common
{
    /// <summary>
    /// 扩展方法类，提供常用的字符串和对象扩展方法
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// 将对象转换为字符串，如果对象为null则返回空字符串
        /// </summary>
        /// <param name="obj">要转换的对象</param>
        /// <returns>对象的字符串表示或空字符串</returns>
        public static string TryToString(this object obj)
        {
            if (obj == null)
                return string.Empty;
            
            return obj.ToString();
        }

        /// <summary>
        /// 检查字符串是否为null、空或仅包含空白字符
        /// </summary>
        /// <param name="str">要检查的字符串</param>
        /// <returns>如果字符串为null、空或仅包含空白字符，则返回true；否则返回false</returns>
        public static bool IsNull(this string str)
        {
            return string.IsNullOrWhiteSpace(str);
        }
    }
}