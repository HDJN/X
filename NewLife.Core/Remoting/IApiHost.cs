﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NewLife.Data;
using NewLife.Messaging;

namespace NewLife.Remoting
{
    /// <summary>Api主机</summary>
    public interface IApiHost
    {
        /// <summary>编码器</summary>
        IEncoder Encoder { get; set; }

        /// <summary>处理器</summary>
        IApiHandler Handler { get; set; }

        /// <summary>过滤器</summary>
        IList<IFilter> Filters { get; }

        /// <summary>接口动作管理器</summary>
        IApiManager Manager { get; }

        /// <summary>是否在会话上复用控制器。复用控制器可确保同一个会话多次请求路由到同一个控制器对象实例</summary>
        Boolean IsReusable { get; }
    }

    /// <summary>Api主机助手</summary>
    public static class ApiHostHelper
    {
        /// <summary>调用</summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="host"></param>
        /// <param name="session"></param>
        /// <param name="action"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        public static async Task<TResult> InvokeAsync<TResult>(IApiHost host, IApiSession session, String action, object args = null)
        {
            if (session == null) return default(TResult);

            var enc = host.Encoder;
            var data = enc.Encode(action, args);

            var msg = session.CreateMessage(data);

            // 过滤器
            host.ExecuteFilter(msg, true);

            var rs = await session.SendAsync(msg);
            if (rs == null) return default(TResult);

            // 过滤器
            host.ExecuteFilter(rs, false);

            // 特殊返回类型
            if (typeof(TResult) == typeof(Packet)) return (TResult)(Object)rs.Payload;

            var dic = enc.Decode(rs.Payload);
            if (typeof(TResult) == typeof(IDictionary<String, Object>)) return (TResult)(Object)dic;

            //return enc.Decode<TResult>(dic);
            var code = 0;
            Object result = null;
            enc.TryGet(dic, out code, out result);

            // 是否成功
            if (code != 0) throw new ApiException(code, result + "");

            if (result == null) return default(TResult);
            if (typeof(TResult) == typeof(Object)) return (TResult)result;

            // 返回
            return enc.Convert<TResult>(result);
        }

        /// <summary>处理消息</summary>
        /// <param name="host"></param>
        /// <param name="session"></param>
        /// <param name="msg"></param>
        /// <returns></returns>
        public static IMessage Process(this IApiHost host, IApiSession session, IMessage msg)
        {
            if (msg.Reply) return null;

            var enc = host.Encoder;

            // 过滤器
            host.ExecuteFilter(msg, false);

            // 这里会导致二次解码，因为解码以后才知道是不是请求
            var dic = enc.Decode(msg.Payload);

            var action = "";
            Object args = null;
            if (!enc.TryGet(dic, out action, out args)) return null;

            object result = null;
            var code = 0;
            try
            {
                result = host.Handler.Execute(session, action, args as IDictionary<String, Object>).Result;
            }
            catch (Exception ex)
            {
                var aex = ex as ApiException;
                code = aex != null ? aex.Code : 1;
                result = ex;
            }

            // 编码响应数据包
            var pk = enc.Encode(code, result);

            // 封装响应消息
            var rs = msg.CreateReply();
            rs.Payload = pk;

            // 过滤器
            host.ExecuteFilter(rs, true);

            return rs;
        }

        /// <summary>执行过滤器</summary>
        /// <param name="host"></param>
        /// <param name="msg"></param>
        /// <param name="issend"></param>
        static void ExecuteFilter(this IApiHost host, IMessage msg, Boolean issend)
        {
            var fs = host.Filters;
            if (fs.Count == 0) return;

            // 接收时需要倒序
            if (!issend) fs = fs.Reverse().ToList();

            var ctx = new ApiFilterContext { Packet = msg.Payload, Message = msg, IsSend = issend };
            foreach (var item in fs)
            {
                item.Execute(ctx);
                //Log.Debug("{0}:{1}", item.GetType().Name, ctx.Packet.ToHex());
            }
        }
    }
}