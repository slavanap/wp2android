// MIT license
// Copyright (C) Vyacheslav Napadovsky, 2017

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace wp2droid4sms {

    public class Converter {
        private static long WpTs2Droid(long wpTs) {
            return wpTs / (10 * 1000) - 11644473600000;
        }

        private static long DroidTs2Wp(long droidTs) {
            return (droidTs + 11644473600000) * (10 * 1000);
        }

        private static long ToDroidTime(DateTime dt) {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return Convert.ToInt64((dt - epoch).TotalMilliseconds);
        }

        private static DateTime FromDroidTime(long droidTs) {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddMilliseconds(droidTs);
        }

        private static XElement CreateDroidSMS(string address, long droidTs, bool incoming, bool read, string body) {
            return new XElement("sms",
                new XAttribute("address", address),
                new XAttribute("body", body),
                new XAttribute("contact_name", "(Unknown)"),
                new XAttribute("date", droidTs.ToString()),
                new XAttribute("date_sent", droidTs.ToString()),
                new XAttribute("locked", "0"),
                new XAttribute("protocol", "0"),
                new XAttribute("read", read ? "1" : "0"),
                new XAttribute("readable_date", FromDroidTime(droidTs).ToString("G")),
                new XAttribute("sc_toa", "null"),
                new XAttribute("service_center", "null"),
                new XAttribute("status", incoming ? "-1" : "0"),
                new XAttribute("subject", "null"),
                new XAttribute("toa", "null"),
                new XAttribute("type", incoming ? "1" : "2")
            );
        }

        public static void ConvertToAndroid(XDocument wpFormatSMS, XDocument wpFormatMMS, out XDocument droidFormat) {
            XElement smses = new XElement("smses");
            var backupTs = DateTime.Now;
            smses.Add(
                new XAttribute("backup_date", ToDroidTime(backupTs).ToString()),
                new XAttribute("backup_set", Guid.NewGuid().ToString()),
                new XAttribute("count", 0)
            );
            int count = 0;
            foreach (var msg in wpFormatSMS.Element("ArrayOfMessage").Elements("Message")) {
                var droidTs = WpTs2Droid(long.Parse(msg.Element("LocalTimestamp").Value));
                bool incoming = bool.Parse(msg.Element("IsIncoming").Value);
                bool read = bool.Parse(msg.Element("IsRead").Value);
                if (incoming) {
                    smses.Add(CreateDroidSMS(msg.Element("Sender").Value, droidTs, incoming, read, msg.Element("Body").Value));
                    count++;
                }
                else {
                    foreach (var recepient in msg.Element("Recepients").Elements("string")) {
                        smses.Add(CreateDroidSMS(recepient.Value, droidTs, incoming, read, msg.Element("Body").Value));
                        count++;
                    }
                }
            }
            var msgTs = backupTs.ToString("yyyyMMddHHmmss");

            foreach (var msg in wpFormatMMS.Element("ArrayOfMessage").Elements("Message")) {
                var mms = new XElement("mms");
                var droidTs = WpTs2Droid(long.Parse(msg.Element("LocalTimestamp").Value));
                bool incoming = bool.Parse(msg.Element("IsIncoming").Value);
                bool read = bool.Parse(msg.Element("IsRead").Value);
                XElement addrs = new XElement("addrs");
                if (incoming) {
                    mms.Add(new XAttribute("address", msg.Element("Sender").Value));
                    addrs.Add(
                        new XElement("addr",
                            new XAttribute("address", msg.Element("Sender").Value),
                            new XAttribute("type", "137"),
                            new XAttribute("charset", "106")
                        ),
                        new XElement("addr",
                            new XAttribute("address", "insert-address-token"),
                            new XAttribute("type", "151"),
                            new XAttribute("charset", "106")
                        )
                    );
                }
                else {
                    string[] addresses = msg.Element("Recepients").Elements("string").Select(s => s.Value).ToArray();
                    mms.Add(new XAttribute("address", string.Join("~", addresses)));
                    addrs.Add(
                        new XElement("addr",
                            new XAttribute("address", "insert-address-token"),
                            new XAttribute("type", "137"),
                            new XAttribute("charset", "106")
                        )
                    );
                    foreach(var address in addresses) {
                        addrs.Add(
                            new XElement("addr",
                                new XAttribute("address", address),
                                new XAttribute("type", "151"),
                                new XAttribute("charset", "106")
                            )
                        );
                    }
                }
                if (!string.IsNullOrWhiteSpace(msg.Element("Body").Value))
                    mms.Add(new XAttribute("body", msg.Element("Body").Value));
                mms.Add(
                    new XAttribute("contact_name", "(Unknown)"),
                    new XAttribute("ct_cls", "null"),
                    new XAttribute("ct_l", "null"),
                    new XAttribute("ct_t", "application/vnd.wap.multipart.related"),
                    new XAttribute("d_rpt", "128"),
                    new XAttribute("d_tm", "null"),
                    new XAttribute("date", droidTs.ToString()),
                    new XAttribute("date_sent", "0"),
                    new XAttribute("exp", "null"),
                    new XAttribute("locked", "0"),
                    new XAttribute("m_cls", "0"),
                    new XAttribute("m_id", $"msg{count:D5}-{msgTs}@localhost"),
                    new XAttribute("m_size", "0"), // will be assigned forward
                    new XAttribute("m_type", "132"),
                    new XAttribute("msg_box", "1"),
                    new XAttribute("pri", "129"),
                    new XAttribute("read", read ? "1" : "0"),
                    new XAttribute("read_status", "null"),
                    new XAttribute("readable_date", FromDroidTime(droidTs).ToString("G")),
                    new XAttribute("resp_st", "null"),
                    new XAttribute("resp_txt", "null"),
                    new XAttribute("retr_st", "null"),
                    new XAttribute("retr_txt", "null"),
                    new XAttribute("retr_txt_cs", "null"),
                    new XAttribute("rpt_a", "null"),
                    new XAttribute("rr", "129"),
                    new XAttribute("seen", read ? "1" : "0"),
                    new XAttribute("st", "null"),
                    new XAttribute("sub", "null"),
                    new XAttribute("sub_cs", "null"),
                    new XAttribute("sub_id", "null"),
                    new XAttribute("text_only", "null"),
                    new XAttribute("tr_id", $"id1_{count:D5}"),
                    new XAttribute("v", "18")
                );
                var parts = new XElement("parts");
                int m_size = 0;
                int partId = 1;
                XElement smil = null;
                int dataCount = 0;
                foreach (var attachment in msg.Element("Attachments").Elements("MessageAttachment")) {
                    var contentType = attachment.Element("AttachmentContentType").Value;
                    var data = attachment.Element("AttachmentDataBase64String").Value;
                    switch (contentType) {
                        case "application/smil":
                            smil = new XElement("part",
                                new XAttribute("cd", "null"),
                                new XAttribute("chset", "null"),
                                new XAttribute("cid", "null"),
                                new XAttribute("cl", "01smil"), // will get replaced at the loop end
                                new XAttribute("ct", contentType),
                                new XAttribute("ctt_s", "null"),
                                new XAttribute("ctt_t", "null"),
                                new XAttribute("fn", "null"),
                                new XAttribute("name", "null"),
                                new XAttribute("seq", "-1"),
                                new XAttribute("text", Encoding.Unicode.GetString(Convert.FromBase64String(data)))
                            );
                            parts.Add(smil);
                            break;
                        case "text/plain": {
                            var text = Encoding.Unicode.GetString(Convert.FromBase64String(data));
                            m_size += Encoding.UTF8.GetBytes(text).Length;
                            parts.Add(new XElement("part",
                                new XAttribute("cd", "null"),
                                new XAttribute("chset", "106"),
                                new XAttribute("cid", "null"),
                                new XAttribute("cl", $"Text{partId:D2}.txt"),
                                new XAttribute("ct", contentType),
                                new XAttribute("ctt_s", "null"),
                                new XAttribute("ctt_t", "null"),
                                new XAttribute("fn", "null"),
                                new XAttribute("name", "null"),
                                new XAttribute("seq", "0"),
                                new XAttribute("text", text)
                            ));
                            break;
                        }
                        default:
                            m_size += Convert.FromBase64String(data).Length;
                            parts.Add(new XElement("part",
                                new XAttribute("cd", "null"),
                                new XAttribute("chset", "null"),
                                new XAttribute("cid", "null"),
                                new XAttribute("cl", $"Image{partId:D2}.txt"),
                                new XAttribute("ct", contentType),
                                new XAttribute("ctt_s", "null"),
                                new XAttribute("ctt_t", "null"),
                                new XAttribute("data", data),
                                new XAttribute("fn", "null"),
                                new XAttribute("name", "null"),
                                new XAttribute("seq", "0"),
                                new XAttribute("text", "null")
                            ));
                            dataCount++;
                            break;
                    }
                    partId++;
                }
                if (smil != null)
                    smil.Attribute("cl").Value = $"{dataCount:D2}smil";
                mms.Attribute("m_size").Value = m_size.ToString();
                mms.Add(parts);
                mms.Add(addrs);
                smses.Add(mms);
                count++;
            }
            smses.Attribute("count").Value = count.ToString();
            droidFormat = new XDocument(smses);
        }

        public static void ConvertToWindowsPhone(XDocument droidFormat, out XDocument wpFormatSMS) {
            var smses = droidFormat.Element("smses");

            var smsResult = new XElement(XNamespace.Xmlns + "ArrayOfMessage",
                new XAttribute(XNamespace.Xmlns + "xsd", XNamespace.Get("http://www.w3.org/2001/XMLSchema")),
                new XAttribute(XNamespace.Xmlns + "xsi", XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance"))
            );
            foreach (var sms in smses.Elements("sms")) {
                var msg = new XElement("Message");
                switch (int.Parse(sms.Attribute("type").Value)) {
                    case 1: // inbox
                        msg.Add(new XElement("Recepients"));
                        msg.Add(new XElement("Body", sms.Attribute("body").Value));
                        msg.Add(new XElement("IsIncoming", true));
                        msg.Add(new XElement("IsRead", int.Parse(sms.Attribute("read").Value) != 0));
                        msg.Add(new XElement("Attachments"));
                        msg.Add(new XElement("LocalTimestamp", DroidTs2Wp(long.Parse(sms.Attribute("date_sent").Value))));
                        msg.Add(new XElement("Sender", sms.Attribute("address").Value));
                        break;
                    case 2: // outbox
                        msg.Add(new XElement("Recepients", new XElement("string", sms.Attribute("address").Value)));
                        msg.Add(new XElement("Body", sms.Attribute("body").Value));
                        msg.Add(new XElement("IsIncoming", false));
                        msg.Add(new XElement("IsRead", int.Parse(sms.Attribute("read").Value) != 0));
                        msg.Add(new XElement("Attachments"));
                        msg.Add(new XElement("LocalTimestamp", DroidTs2Wp(long.Parse(sms.Attribute("date_sent").Value))));
                        msg.Add(new XElement("Sender"));
                        break;
                    default:
                        throw new ArgumentException("Invalid message type");
                }
                smsResult.Add(msg);
            }
            wpFormatSMS = new XDocument(smsResult);
        }

        static void Main(string[] args) {
            const string smsBackup = @".\smsBackup\Сб, июл 29 2017, 18-49-28 .msg";
            const string mmsBackup = @".\mmsBackup\Сб, июл 29 2017, 18-50-14 .msg";
            const string droidBackup = @".\";
            XDocument result;
            ConvertToAndroid(XDocument.Parse(File.ReadAllText(smsBackup)), XDocument.Parse(File.ReadAllText(mmsBackup)), out result);
            File.WriteAllText(droidBackup + $"sms-{DateTime.Now:yyyyMMddHHmmss}.xml", result.ToString());
        }
    }
}
