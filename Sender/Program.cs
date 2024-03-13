using API2;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Quartz.Impl;
using ReportsReceiver.Domain;
using System.Diagnostics;
using System.Net.Sockets;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

class Program
{
    public static async Task Main(string[] args)
    {
        // Grab the Scheduler instance from the Factory
        StdSchedulerFactory factory = new StdSchedulerFactory();
        IScheduler scheduler = await factory.GetScheduler();
        // and start it off
        await scheduler.Start();
        // define the job and tie it to our class
        IJobDetail job = JobBuilder.Create<CheckForNewreportsJob>()
            .WithIdentity("job1", "group1")
            .Build();

        // Trigger the job to run now, and then repeat every 10 seconds
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .StartNow()
            .WithCronSchedule("0/30 * * * * ?")
            .Build();

        // Tell quartz to schedule the job using our trigger
        await scheduler.ScheduleJob(job, trigger);
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(100));
        };
        // and last shut down the scheduler when you are ready to close your program
        await scheduler.Shutdown();

    }
}

[DisallowConcurrentExecution]
public class CheckForNewreportsJob : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        int ourPeriod= await PeriodNow(0);
        int periodIn8Minutes = await PeriodNow(8);
        double canSave = await HowManyMbCanSave();
        long canSaveBytes = (long)(canSave * 125000);
        // для теста
        //canSaveBytes = 100000;
        //canSaveBytes = 1000000000000;
        Console.WriteLine($"Доступно до закрытия канала: {canSaveBytes}");
        int reportId = await findReport();
        long needSpace;
        int status;
        if (reportId != 0) {
            using (ApplicationContext db = new ApplicationContext())
            {
                Report report = db.Reports.Where(p => p.Id == reportId).FirstOrDefault();
                needSpace = (long)(report.Filesize - report.BytesSend);
                status = report.Status;
            }
            if (needSpace <= canSaveBytes && status==0 && ourPeriod==periodIn8Minutes && ourPeriod!=0)
            {
                await SendAllReport(reportId);
                using (ApplicationContext db = new ApplicationContext())
                {
                    Report report = db.Reports.Where(p => p.Id == reportId).FirstOrDefault();
                    if (report.Status==2)
                    {
                        report.Status = 0;
                        await db.SaveChangesAsync();
                        Console.WriteLine("Error. Bad time gap. Try again later");
                    }
                }

            }
            else if (canSaveBytes > 0 && ourPeriod == periodIn8Minutes && ourPeriod != 0)
            {
                long bytesSent = 0;
                bytesSent = await SendPartReport((long)canSaveBytes, reportId);
                using (ApplicationContext db = new ApplicationContext())
                {
                    Report report = db.Reports.Where(p => p.Id == reportId).FirstOrDefault();
                    if (bytesSent==0)
                    {
                        report.Status = 0;
                        Console.WriteLine("Error. Bad time gap. Try again later");
                    }
                    else if (report.BytesSend + bytesSent < report.Filesize)
                    {
                        report.Status = 2;
                    }
                    else
                    {
                        report.Status = 1;
                        report.DateReceived = DateTime.Now;
                    }
                    report.BytesSend += bytesSent;
                    Console.WriteLine($"Отправлено байт: {report.BytesSend}");
                    await db.SaveChangesAsync();
                }

            }
            else
            {
                Console.WriteLine("Bad time gap. Try again later");
            }

        }



    }
    public async static Task<double> HowManyMbCanSave()
    {
        double res = 0;
        using (ApplicationContext db = new ApplicationContext())
        {
            List<PeriodTableModel> periods = db.Periods.ToList();
            DateTime time = DateTime.Now.AddMinutes(8);
            int ourPeriod = periods.Count - 1;
            for (int i = 0; i < periods.Count; i++)
            {
                if (periods[i].From > time)
                {
                    if (i == 0)
                    {
                        return res;
                    }
                    else
                    {
                        ourPeriod = i - 1;
                    }
                    break;
                }
            }
            DateTime periodTo = periods[ourPeriod].To;
            if (periods[ourPeriod].To > time)
            {
                TimeSpan diff = periodTo - time;
                res = diff.TotalSeconds * periods[ourPeriod].Speed;
            }

        }
        return res;
    }
    public async static Task<int> PeriodNow(int minutes_from)
    {
        int res = 0;
        using (ApplicationContext db = new ApplicationContext())
        {
            List<PeriodTableModel> periods = db.Periods.ToList();
            DateTime time = DateTime.Now;
            if (minutes_from > 0)
            {
                time.AddMinutes(minutes_from);
            }
            int ourPeriod = periods.Count - 1;
            for (int i = 0; i < periods.Count; i++)
            {
                if (periods[i].From > time)
                {
                    if (i == 0)
                    {
                        return res;
                    }
                    else
                    {
                        ourPeriod = i - 1;
                    }
                    break;
                }
            }
            DateTime periodTo = periods[ourPeriod].To;
            if (periods[ourPeriod].To > time)
            {
                res = ourPeriod;
            }

        }
        return res;
    }
    public async static Task<int> findReport()
    {
        int id = await findReportUnfinished();
        if (id != 0)
        {
            return id;
        }
        using (ApplicationContext db = new ApplicationContext())
        {
            Report report = db.Reports.Where(p => p.Status == 0).FirstOrDefault();
            if (report!= null)
            {
                id = report.Id;
                return id;
            }
        }
        return 0;
       }
    public async static Task<int> findReportUnfinished()
    {
        using (ApplicationContext db = new ApplicationContext())
        {
            Report? report = db.Reports.Where(p => p.Status == 2).FirstOrDefault();
            if (report == null)
            {
                return 0;
            }
                return report.Id;
        }

    }
    public async static Task SendAllReport(int reportId)
    {
        Console.WriteLine("Start sending file");
        string SenderName;
        string ReportName;
        string Filename ="";
        int Status=0;
        double Filesize;
        double BytesSend;
        long needSpace = 0;
        int Description;
        string TextContent;
        string dateCreated;
        string dateSent;
        DateTime dateReceived;
        using (ApplicationContext db = new ApplicationContext())
        {
            Report report = db.Reports.Where(p => p.Id == reportId).FirstOrDefault();
            report.DateSent = DateTime.Now;
            needSpace = (long)(report.Filesize - report.BytesSend);
            SenderName = report.SenderName;
            ReportName = report.ReportName;
            if (report.Filename!=null && report.Filename != "")
            {
                Filename = report.Filename;
            }
            ReportTextContent content = db.ReportTextContents.Where(p => p.Id == report.TextContent_Id).FirstOrDefault();
            Description = (int)report.ReportDescription_Id;
            TextContent = content.TextContent;
            dateCreated = report.DateCreated.ToString();
            report.Status = 2;
            await db.SaveChangesAsync();
            dateSent = report.DateSent.ToString();
        }

        using TcpClient tcpClient = new TcpClient();
        await tcpClient.ConnectAsync("127.0.0.1", 8888);
        // получаем NetworkStream для взаимодействия с сервером
        var stream = tcpClient.GetStream(); ;
        // создаем BinaryReader для чтения данных
        using var binaryReader = new BinaryReader(stream);

        using var binaryWriter = new BinaryWriter(stream);

        // отправляем данные товара
        binaryWriter.Write(needSpace);
        binaryWriter.Write(Status);
        binaryWriter.Write(SenderName);
        binaryWriter.Write(ReportName);
        binaryWriter.Write(Filename);
        binaryWriter.Write(Description);
        binaryWriter.Write(TextContent);
        binaryWriter.Write(dateCreated);
        binaryWriter.Write(dateSent);
        string path = "../api2/wwwroot/files/" + Filename;
        long fileSize = 0;
        if (Filename != "")
        {
            using (FileStream fileStream = File.OpenRead(path))
            {
                fileSize = new FileInfo(path).Length;
                binaryWriter.Write(fileSize);

                byte[] buffer = new byte[1024];
                int bytesRead;

                while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    binaryWriter.Write(buffer, 0, bytesRead);
                }

                Console.WriteLine("File sent successfully.");
            }
        } else
        {
            Console.WriteLine("Report sent successfully.");
        }
        binaryWriter.Flush();

        // считываем сгенерированный на сервере id нового товара
        var state = binaryReader.ReadBoolean();
        if (state)
        {
            Console.WriteLine($"Report received.");
            dateReceived = DateTime.Parse(binaryReader.ReadString());
            using (ApplicationContext db = new ApplicationContext())
            {
                Report report = db.Reports.Where(p => p.Id == reportId).FirstOrDefault();
                report.DateReceived= dateReceived;
                report.Status = 1;
                report.BytesSend = report.Filesize;
                await db.SaveChangesAsync();
            }
        }
    }
    public async static Task<long> SendPartReport(long haveSpace, int reportId)
    {
        Console.WriteLine("Start sending file part");
        // данные для отправки
        string SenderName;
        string ReportName;
        string Filename="";
        int Status;
        double Filesize;
        double BytesSend;
        long needSpace = 0;
        int Description;
        string TextContent;
        string dateCreated;
        string dateSent;
        DateTime dateReceived;
        using (ApplicationContext db = new ApplicationContext())
        {
            Report report = db.Reports.Where(p => p.Id == reportId).FirstOrDefault();
            report.DateSent = DateTime.Now;
            needSpace = (long)(report.Filesize - report.BytesSend);
            SenderName = report.SenderName;
            ReportName = report.ReportName;
            if (report.Filename != null && report.Filename != "")
            {
                Filename = report.Filename;
            }
            Status = report.Status;
            Filesize = report.Filesize;
            BytesSend= report.BytesSend;
            dateCreated = report.DateCreated.ToString();
            ReportTextContent content = db.ReportTextContents.Where(p => p.Id == report.TextContent_Id).FirstOrDefault();
            Description = (int)report.ReportDescription_Id;
            TextContent = content.TextContent;
            await db.SaveChangesAsync();
            dateSent = report.DateSent.ToString();
        }
        using (ApplicationContext db = new ApplicationContext())
        {
            Report report = db.Reports.Where(p => p.Id == reportId).FirstOrDefault();
            report.Status = 2;
            await db.SaveChangesAsync();
        }
        long willsend = await willSendSize(haveSpace, (long)BytesSend, (long)Filesize);
            Console.WriteLine($"Собираемся послать: {willsend}");
        using TcpClient tcpClient = new TcpClient();
        await tcpClient.ConnectAsync("127.0.0.1", 8888);
        // получаем NetworkStream для взаимодействия с сервером
        var stream = tcpClient.GetStream(); ;
        // создаем BinaryReader для чтения данных
        using var binaryReader = new BinaryReader(stream);

        using var binaryWriter = new BinaryWriter(stream);

        // отправляем данные товара
        binaryWriter.Write(haveSpace);
        binaryWriter.Write(Status);
        binaryWriter.Write(SenderName);
        binaryWriter.Write(ReportName);
        binaryWriter.Write(Filename);
        binaryWriter.Write(Description);
        binaryWriter.Write(TextContent);
        binaryWriter.Write(dateCreated);
        binaryWriter.Write(dateSent);
        long bytesSent = 0;

        string path = "../api2/wwwroot/files/" + Filename;
        using (FileStream fileStream = File.OpenRead(path))
        {
            long fileSize = new FileInfo(path).Length;
            binaryWriter.Write(willsend);

            byte[] buffer = new byte[1024];
            int bytesRead;
            if (Status==2)
            {
                fileStream.Seek((long)BytesSend, SeekOrigin.Begin);
            }
            while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) > 0 && (bytesSent+buffer.Length) <= (willsend))
            {
                binaryWriter.Write(buffer, 0, bytesRead);
                bytesSent += bytesRead;
            }
            binaryWriter.Write(buffer, 0, (int)(willsend-bytesSent));
            bytesSent += (int)(willsend - bytesSent);
            Console.WriteLine("File part sent successfully.");
            var state = binaryReader.ReadBoolean();
            if (state)
            {
                Console.WriteLine($"Report part received.");
                dateReceived = DateTime.Parse(binaryReader.ReadString());
                using (ApplicationContext db = new ApplicationContext())
                {
                    Report report = db.Reports.Where(p => p.Id == reportId).FirstOrDefault();
                    report.DateReceived = dateReceived;
                    await db.SaveChangesAsync();
                }
            } else
            {
                bytesSent = 0;
            }
        }
        binaryWriter.Flush();


        return bytesSent;
    }
    public async static Task<long> willSendSize (long haveSpace, long wasSend, long fileSize)
    {
            return Math.Min(haveSpace, fileSize - wasSend);
    }
}

