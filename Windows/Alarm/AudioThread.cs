using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace MacGuard.Alarm;

/// <summary>
/// Ses işlerinin (WASAPI / Core Audio COM nesneleri) tek iş parçacığı.
///
/// Core Audio nesneleri oluşturuldukları COM dairesine bağlı. Arayüz
/// iş parçacığı STA, zamanlayıcılar MTA; aynı nesneye ikisinden birden
/// dokunmak "COM nesnesi arayüze dönüştürülemedi" hatası verebiliyor.
/// Bütün ses çağrıları bu tek MTA iş parçacığında sırayla çalışır.
/// </summary>
internal static class AudioThread
{
    private static readonly BlockingCollection<Action> Queue = new();

    static AudioThread()
    {
        var t = new Thread(() =>
        {
            foreach (var work in Queue.GetConsumingEnumerable())
            {
                try { work(); }
                catch (Exception e) { System.Diagnostics.Debug.WriteLine("MacGuard: ses işi başarısız — " + e.Message); }
            }
        })
        {
            IsBackground = true,
            Name = "MacGuard.Audio",
        };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();
    }

    public static void Post(Action work) => Queue.Add(work);

    /// <summary>İşi ses iş parçacığında çalıştırıp sonucunu bekler.</summary>
    public static T Invoke<T>(Func<T> work, T fallback, int timeoutMs = 3000)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Add(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception e) { tcs.SetException(e); }
        });
        try
        {
            return tcs.Task.Wait(timeoutMs) ? tcs.Task.Result : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
