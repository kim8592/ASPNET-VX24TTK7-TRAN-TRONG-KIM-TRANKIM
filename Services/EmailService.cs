using System.Text.Encodings.Web;
using System.Globalization;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MilkTeaWeb.Models.Enums;
using MilkTeaWeb.Options;

namespace MilkTeaWeb.Services;

public class EmailService(IOptions<SmtpOptions> smtpOptions, ILogger<EmailService> logger)
{
    public Task SendConfirmationEmailAsync(string recipientEmail, string confirmationUrl) =>
        SendAsync(
            recipientEmail,
            "Xác nhận email MilkTeaWeb",
            "Chào mừng bạn đến với MilkTeaWeb!",
            "Xác nhận email",
            "Vui lòng xác nhận email để hoàn tất đăng ký MilkTeaWeb.",
            "Nếu bạn không tạo tài khoản này, bạn có thể bỏ qua email này.",
            confirmationUrl);

    public Task SendPasswordResetEmailAsync(string recipientEmail, string resetUrl) =>
        SendAsync(
            recipientEmail,
            "Đặt lại mật khẩu MilkTeaWeb",
            "Yêu cầu đặt lại mật khẩu",
            "Đặt lại mật khẩu",
            "Bạn đã yêu cầu đặt lại mật khẩu cho tài khoản MilkTeaWeb.",
            "Nếu bạn không thực hiện yêu cầu này, hãy bỏ qua email và kiểm tra bảo mật tài khoản của bạn.",
            resetUrl);

    public async Task SendOrderConfirmationEmailAsync(OrderConfirmationEmail message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message.RecipientEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.DetailsUrl);

        var emailMessage = new MimeMessage();
        var settings = smtpOptions.Value;
        emailMessage.From.Add(new MailboxAddress(settings.FromName, settings.FromEmail));
        emailMessage.To.Add(MailboxAddress.Parse(message.RecipientEmail));
        emailMessage.Subject = $"Xác nhận đơn hàng #{message.OrderId} | MilkTeaWeb";
        emailMessage.Body = new BodyBuilder
        {
            TextBody = BuildOrderConfirmationText(message),
            HtmlBody = BuildOrderConfirmationHtml(message)
        }.ToMessageBody();

        await SendMessageAsync(emailMessage);
    }

    public async Task SendOrderStatusNotificationEmailAsync(OrderStatusNotificationEmail message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message.RecipientEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.DetailsUrl);

        var emailMessage = new MimeMessage();
        var settings = smtpOptions.Value;
        emailMessage.From.Add(new MailboxAddress(settings.FromName, settings.FromEmail));
        emailMessage.To.Add(MailboxAddress.Parse(message.RecipientEmail));
        emailMessage.Subject = $"Cập nhật đơn hàng #{message.OrderId}: {message.StatusLabel} | MilkTeaWeb";
        emailMessage.Body = new BodyBuilder
        {
            TextBody = BuildOrderStatusNotificationText(message),
            HtmlBody = BuildOrderStatusNotificationHtml(message)
        }.ToMessageBody();

        await SendMessageAsync(emailMessage);
    }

    private async Task SendAsync(
        string recipientEmail,
        string subject,
        string heading,
        string actionLabel,
        string message,
        string securityNote,
        string actionUrl)
    {
        var emailMessage = new MimeMessage();
        var settings = smtpOptions.Value;
        emailMessage.From.Add(new MailboxAddress(settings.FromName, settings.FromEmail));
        emailMessage.To.Add(MailboxAddress.Parse(recipientEmail));
        emailMessage.Subject = subject;
        emailMessage.Body = new BodyBuilder
        {
            TextBody = $"{heading}\n\n{message}\n\n{actionLabel}: {actionUrl}\n\n{securityNote}\n\nTrân trọng,\nĐội ngũ MilkTeaWeb",
            HtmlBody = BuildHtmlBody(heading, message, actionLabel, securityNote, actionUrl)
        }.ToMessageBody();

        await SendMessageAsync(emailMessage);
    }

    private async Task SendMessageAsync(MimeMessage emailMessage)
    {
        var settings = smtpOptions.Value;
        ValidateSettings(settings);

        try
        {
            using var smtpClient = new SmtpClient();
            var socketOptions = settings.UseStartTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.Auto;

            await smtpClient.ConnectAsync(settings.Host, settings.Port, socketOptions);
            await smtpClient.AuthenticateAsync(settings.UserName, settings.Password);
            await smtpClient.SendAsync(emailMessage);
            await smtpClient.DisconnectAsync(true);
        }
        catch
        {
            logger.LogError("SMTP email delivery failed.");
            throw;
        }
    }

    private static string BuildOrderConfirmationText(OrderConfirmationEmail message)
    {
        var lines = new List<string>
        {
            $"Chào {message.RecipientName},",
            string.Empty,
            $"MilkTeaWeb đã nhận đơn hàng #{message.OrderId} của bạn.",
            $"Trạng thái hiện tại: {message.StatusLabel}",
            $"Thời gian đặt: {FormatDate(message.CreatedAt)}",
            string.Empty,
            "Thông tin nhận hàng",
            $"Hình thức: {FormatFulfillment(message.FulfillmentMethod)}",
            $"Cửa hàng: {message.StoreName}",
            $"Địa chỉ cửa hàng: {message.StoreAddress}",
            $"Điện thoại cửa hàng: {message.StorePhone}",
            $"Người nhận: {message.RecipientName} - {message.RecipientPhone}"
        };

        if (message.FulfillmentMethod == FulfillmentMethod.Delivery
            && !string.IsNullOrWhiteSpace(message.DeliveryAddress))
        {
            lines.Add($"Địa chỉ giao hàng: {message.DeliveryAddress}");
        }

        lines.Add($"Thanh toán: {FormatPayment(message.PaymentMethod)}");
        if (!string.IsNullOrWhiteSpace(message.OrderNote))
        {
            lines.Add($"Ghi chú: {message.OrderNote}");
        }

        lines.Add(string.Empty);
        lines.Add("Sản phẩm đã chọn");
        foreach (var item in message.Items)
        {
            lines.Add($"- {item.ProductName} ({FormatConfiguration(item)})");
            lines.Add($"  Giá cơ bản: {FormatMoney(item.UnitPrice)} / món · Số lượng: {item.Quantity}");
            foreach (var topping in item.Toppings)
            {
                lines.Add($"  Topping: {topping.Name} +{FormatMoney(topping.UnitPrice)} / món");
            }

            lines.Add($"  Thành tiền: {FormatMoney(item.LineTotal)}");
        }

        lines.Add(string.Empty);
        lines.Add($"Tạm tính: {FormatMoney(message.Subtotal)}");
        lines.Add($"Phí giao hàng: {FormatMoney(message.DeliveryFee)}");
        lines.Add($"Tổng cộng: {FormatMoney(message.TotalAmount)}");
        lines.Add(string.Empty);
        lines.Add($"Xem đơn hàng: {message.DetailsUrl}");
        lines.Add(string.Empty);
        lines.Add("Cảm ơn bạn đã chọn MilkTeaWeb.");

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildOrderConfirmationHtml(OrderConfirmationEmail message)
    {
        var encoder = HtmlEncoder.Default;
        var items = string.Join(string.Empty, message.Items.Select(item =>
        {
            var toppingRows = string.Join(string.Empty, item.Toppings.Select(topping =>
                $"<li>{encoder.Encode(topping.Name)} <strong>+{encoder.Encode(FormatMoney(topping.UnitPrice))} / món</strong></li>"));
            var toppings = item.Toppings.Count == 0
                ? string.Empty
                : $"<ul style=\"margin:10px 0 0;padding-left:18px;color:#505b55;font-size:14px;\">{toppingRows}</ul>";

            return $"""
                <tr>
                  <td style="padding:16px 0;border-bottom:1px solid #e5e1d8;">
                    <div style="font-size:16px;font-weight:700;color:#252a28;">{encoder.Encode(item.ProductName)}</div>
                    <div style="margin-top:4px;color:#68716d;font-size:14px;">{encoder.Encode(FormatConfiguration(item))}</div>
                    <div style="margin-top:8px;color:#505b55;font-size:14px;">Giá cơ bản: {encoder.Encode(FormatMoney(item.UnitPrice))} / món · Số lượng: {item.Quantity}</div>
                    {toppings}
                    <div style="margin-top:10px;color:#1f5a45;font-size:15px;font-weight:700;">Thành tiền: {encoder.Encode(FormatMoney(item.LineTotal))}</div>
                  </td>
                </tr>
                """;
        }));

        var deliveryAddress = message.FulfillmentMethod == FulfillmentMethod.Delivery
            && !string.IsNullOrWhiteSpace(message.DeliveryAddress)
                ? $"<tr><td style=\"padding:5px 0;color:#68716d;vertical-align:top;\">Địa chỉ giao hàng</td><td style=\"padding:5px 0;color:#252a28;text-align:right;\">{encoder.Encode(message.DeliveryAddress)}</td></tr>"
                : string.Empty;
        var note = string.IsNullOrWhiteSpace(message.OrderNote)
            ? string.Empty
            : $"<p style=\"margin:16px 0 0;padding:12px 14px;background:#fbf8f2;border-left:3px solid #c98b3e;color:#505b55;font-size:14px;\"><strong>Ghi chú:</strong> {encoder.Encode(message.OrderNote)}</p>";

        return $"""
            <!doctype html>
            <html lang="vi">
              <body style="margin:0;background:#fbf8f2;color:#252a28;font-family:Arial,'Segoe UI',sans-serif;line-height:1.6;">
                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#fbf8f2;padding:32px 16px;">
                  <tr>
                    <td align="center">
                      <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:640px;background:#ffffff;border:1px solid #e5e1d8;border-radius:12px;overflow:hidden;">
                        <tr><td style="padding:24px 32px;background:#1f5a45;color:#ffffff;"><div style="font-size:22px;font-weight:700;">MilkTeaWeb</div><div style="margin-top:4px;font-size:13px;color:#dcebe4;">Xác nhận đơn hàng</div></td></tr>
                        <tr>
                          <td style="padding:32px;">
                            <h1 style="margin:0 0 10px;font-size:25px;line-height:1.25;">Đơn hàng #{message.OrderId} đã được tiếp nhận</h1>
                            <p style="margin:0;color:#505b55;font-size:16px;">Chào {encoder.Encode(message.RecipientName)}, cảm ơn bạn đã đặt món tại MilkTeaWeb.</p>
                            <div style="margin:22px 0;padding:14px 16px;border:1px solid #cce2d8;border-radius:10px;background:#eef7f2;"><div style="color:#68716d;font-size:12px;font-weight:700;text-transform:uppercase;letter-spacing:.06em;">Trạng thái đơn hàng</div><div style="margin-top:3px;color:#1f5a45;font-size:20px;font-weight:700;">{encoder.Encode(message.StatusLabel)}</div><div style="margin-top:2px;color:#68716d;font-size:13px;">Đặt lúc {encoder.Encode(FormatDate(message.CreatedAt))}</div></div>
                            <h2 style="margin:0 0 10px;font-size:18px;">Thông tin nhận hàng</h2>
                            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="font-size:14px;"><tbody>
                              <tr><td style="padding:5px 0;color:#68716d;">Hình thức</td><td style="padding:5px 0;color:#252a28;text-align:right;">{encoder.Encode(FormatFulfillment(message.FulfillmentMethod))}</td></tr>
                              <tr><td style="padding:5px 0;color:#68716d;vertical-align:top;">Cửa hàng</td><td style="padding:5px 0;color:#252a28;text-align:right;"><strong>{encoder.Encode(message.StoreName)}</strong><br>{encoder.Encode(message.StoreAddress)}<br>{encoder.Encode(message.StorePhone)}</td></tr>
                              <tr><td style="padding:5px 0;color:#68716d;">Người nhận</td><td style="padding:5px 0;color:#252a28;text-align:right;">{encoder.Encode(message.RecipientName)}<br>{encoder.Encode(message.RecipientPhone)}</td></tr>
                              {deliveryAddress}
                              <tr><td style="padding:5px 0;color:#68716d;">Thanh toán</td><td style="padding:5px 0;color:#252a28;text-align:right;">{encoder.Encode(FormatPayment(message.PaymentMethod))}</td></tr>
                            </tbody></table>
                            {note}
                            <h2 style="margin:28px 0 0;font-size:18px;">Sản phẩm đã chọn</h2>
                            <table role="presentation" width="100%" cellspacing="0" cellpadding="0">{items}</table>
                            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="margin-top:18px;font-size:15px;"><tbody>
                              <tr><td style="padding:5px 0;color:#68716d;">Tạm tính</td><td style="padding:5px 0;text-align:right;">{encoder.Encode(FormatMoney(message.Subtotal))}</td></tr>
                              <tr><td style="padding:5px 0;color:#68716d;">Phí giao hàng</td><td style="padding:5px 0;text-align:right;">{encoder.Encode(FormatMoney(message.DeliveryFee))}</td></tr>
                              <tr><td style="padding:12px 0 0;border-top:1px solid #e5e1d8;font-size:18px;font-weight:700;">Tổng cộng</td><td style="padding:12px 0 0;border-top:1px solid #e5e1d8;color:#1f5a45;text-align:right;font-size:18px;font-weight:700;">{encoder.Encode(FormatMoney(message.TotalAmount))}</td></tr>
                            </tbody></table>
                            <table role="presentation" cellspacing="0" cellpadding="0" style="margin-top:28px;"><tr><td style="border-radius:8px;background:#1f5a45;"><a href="{encoder.Encode(message.DetailsUrl)}" style="display:inline-block;padding:13px 22px;border:1px solid #1f5a45;border-radius:8px;color:#ffffff;font-size:15px;font-weight:700;text-decoration:none;">Xem đơn hàng</a></td></tr></table>
                          </td>
                        </tr>
                      </table>
                    </td>
                  </tr>
                </table>
              </body>
            </html>
            """;
    }

    private static string BuildOrderStatusNotificationText(OrderStatusNotificationEmail message)
    {
        var lines = new List<string>
        {
            $"Chào {message.RecipientName},",
            string.Empty,
            $"Đơn hàng #{message.OrderId} vừa được cập nhật.",
            $"Trạng thái: {message.StatusLabel}",
            message.StatusMessage,
            string.Empty,
            $"Cửa hàng: {message.StoreName}",
            $"Địa chỉ cửa hàng: {message.StoreAddress}",
            $"Điện thoại cửa hàng: {message.StorePhone}"
        };

        if (message.FulfillmentMethod == FulfillmentMethod.Delivery
            && !string.IsNullOrWhiteSpace(message.DeliveryAddress))
        {
            lines.Add($"Địa chỉ giao hàng: {message.DeliveryAddress}");
        }

        lines.Add(string.Empty);
        lines.Add($"Xem chi tiết đơn hàng: {message.DetailsUrl}");
        lines.Add(string.Empty);
        lines.Add("Trân trọng,");
        lines.Add("Đội ngũ MilkTeaWeb");
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildOrderStatusNotificationHtml(OrderStatusNotificationEmail message)
    {
        var encoder = HtmlEncoder.Default;
        var deliveryAddress = message.FulfillmentMethod == FulfillmentMethod.Delivery
            && !string.IsNullOrWhiteSpace(message.DeliveryAddress)
                ? $"<tr><td style=\"padding:7px 0;color:#68716d;vertical-align:top;\">Địa chỉ giao hàng</td><td style=\"padding:7px 0;color:#252a28;text-align:right;\">{encoder.Encode(message.DeliveryAddress)}</td></tr>"
                : string.Empty;

        return $"""
            <!doctype html>
            <html lang="vi">
              <body style="margin:0;background:#fbf8f2;color:#252a28;font-family:Arial,'Segoe UI',sans-serif;line-height:1.6;">
                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#fbf8f2;padding:32px 16px;">
                  <tr><td align="center">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:600px;background:#ffffff;border:1px solid #e5e1d8;border-radius:12px;overflow:hidden;">
                      <tr><td style="padding:22px 30px;background:#1f5a45;color:#ffffff;font-size:21px;font-weight:700;">MilkTeaWeb</td></tr>
                      <tr><td style="padding:30px;">
                        <p style="margin:0 0 8px;color:#68716d;font-size:14px;">Chào {encoder.Encode(message.RecipientName)},</p>
                        <h1 style="margin:0;font-size:24px;line-height:1.3;">Đơn hàng #{message.OrderId} có cập nhật mới</h1>
                        <div style="margin:20px 0;padding:16px;border:1px solid #cce2d8;border-radius:10px;background:#eef7f2;">
                          <div style="color:#68716d;font-size:12px;font-weight:700;letter-spacing:.06em;text-transform:uppercase;">Trạng thái đơn hàng</div>
                          <div style="margin-top:4px;color:#1f5a45;font-size:21px;font-weight:700;">{encoder.Encode(message.StatusLabel)}</div>
                          <p style="margin:8px 0 0;color:#252a28;">{encoder.Encode(message.StatusMessage)}</p>
                        </div>
                        <h2 style="margin:0 0 8px;font-size:17px;">Thông tin nhận hàng</h2>
                        <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="font-size:14px;"><tbody>
                          <tr><td style="padding:7px 0;color:#68716d;">Hình thức</td><td style="padding:7px 0;color:#252a28;text-align:right;">{encoder.Encode(FormatFulfillment(message.FulfillmentMethod))}</td></tr>
                          <tr><td style="padding:7px 0;color:#68716d;vertical-align:top;">Cửa hàng</td><td style="padding:7px 0;color:#252a28;text-align:right;"><strong>{encoder.Encode(message.StoreName)}</strong><br>{encoder.Encode(message.StoreAddress)}<br>{encoder.Encode(message.StorePhone)}</td></tr>
                          {deliveryAddress}
                        </tbody></table>
                        <table role="presentation" cellspacing="0" cellpadding="0" style="margin-top:24px;"><tr><td style="border-radius:8px;background:#1f5a45;"><a href="{encoder.Encode(message.DetailsUrl)}" style="display:inline-block;padding:12px 20px;border:1px solid #1f5a45;border-radius:8px;color:#ffffff;font-size:15px;font-weight:700;text-decoration:none;">Xem đơn hàng</a></td></tr></table>
                        <p style="margin:24px 0 0;color:#68716d;font-size:13px;">Trân trọng,<br><strong style="color:#1f5a45;">Đội ngũ MilkTeaWeb</strong></p>
                      </td></tr>
                    </table>
                  </td></tr>
                </table>
              </body>
            </html>
            """;
    }

    private static string FormatConfiguration(OrderConfirmationEmailItem item)
    {
        var options = new List<string> { item.SizeName };
        if (item.SugarPercentage.HasValue)
        {
            options.Add($"Đường {item.SugarPercentage}%");
        }

        if (item.IcePercentage.HasValue)
        {
            options.Add($"Đá {item.IcePercentage}%");
        }

        return string.Join(" · ", options);
    }

    private static string FormatMoney(decimal amount) =>
        string.Format(CultureInfo.GetCultureInfo("vi-VN"), "{0:N0} đ", amount);

    private static string FormatDate(DateTime createdAt) =>
        createdAt.ToLocalTime().ToString("dd/MM/yyyy, HH:mm", CultureInfo.GetCultureInfo("vi-VN"));

    private static string FormatFulfillment(FulfillmentMethod fulfillmentMethod) =>
        fulfillmentMethod == FulfillmentMethod.Pickup ? "Nhận tại cửa hàng" : "Giao tận nơi";

    private static string FormatPayment(PaymentMethod paymentMethod) =>
        paymentMethod == PaymentMethod.PayAtStore ? "Thanh toán tại cửa hàng" : "Tiền mặt khi nhận hàng (COD)";

    private static string BuildHtmlBody(
        string heading,
        string message,
        string actionLabel,
        string securityNote,
        string actionUrl)
    {
        var encoder = HtmlEncoder.Default;
        var encodedHeading = encoder.Encode(heading);
        var encodedMessage = encoder.Encode(message);
        var encodedActionLabel = encoder.Encode(actionLabel);
        var encodedSecurityNote = encoder.Encode(securityNote);
        var encodedUrl = encoder.Encode(actionUrl);

        return $"""
            <!doctype html>
            <html lang="vi">
              <body style="margin:0;background:#fbf8f2;color:#252a28;font-family:Arial,'Segoe UI',sans-serif;line-height:1.6;">
                <div style="display:none;max-height:0;overflow:hidden;opacity:0;">{encodedHeading}</div>
                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#fbf8f2;padding:32px 16px;">
                  <tr>
                    <td align="center">
                      <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:600px;background:#ffffff;border:1px solid #e5e1d8;border-radius:12px;overflow:hidden;">
                        <tr>
                          <td style="padding:24px 32px;background:#1f5a45;color:#ffffff;">
                            <div style="font-size:22px;font-weight:700;letter-spacing:-.02em;">MilkTeaWeb</div>
                            <div style="margin-top:4px;font-size:13px;color:#dcebe4;">Nền tảng đặt trà sữa</div>
                          </td>
                        </tr>
                        <tr>
                          <td style="padding:32px;">
                            <h1 style="margin:0 0 14px;color:#252a28;font-size:25px;line-height:1.25;">{encodedHeading}</h1>
                            <p style="margin:0 0 24px;color:#505b55;font-size:16px;">{encodedMessage}</p>
                            <table role="presentation" cellspacing="0" cellpadding="0">
                              <tr>
                                <td style="border-radius:8px;background:#1f5a45;">
                                  <a href="{encodedUrl}" style="display:inline-block;padding:13px 22px;border:1px solid #1f5a45;border-radius:8px;color:#ffffff;font-size:15px;font-weight:700;text-decoration:none;">{encodedActionLabel}</a>
                                </td>
                              </tr>
                            </table>
                            <p style="margin:24px 0 0;color:#68716d;font-size:13px;">Nếu nút không hoạt động, hãy sao chép liên kết trong email và mở bằng trình duyệt.</p>
                            <div style="margin-top:24px;padding:14px 16px;border-left:3px solid #c98b3e;background:#fff9e9;color:#72541a;font-size:13px;">{encodedSecurityNote}</div>
                          </td>
                        </tr>
                        <tr>
                          <td style="padding:20px 32px;border-top:1px solid #e5e1d8;color:#68716d;font-size:12px;">
                            Trân trọng,<br><strong style="color:#1f5a45;">Đội ngũ MilkTeaWeb</strong>
                          </td>
                        </tr>
                      </table>
                    </td>
                  </tr>
                </table>
              </body>
            </html>
            """;
    }

    private static void ValidateSettings(SmtpOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Host)
            || settings.Port is < 1 or > 65535
            || string.IsNullOrWhiteSpace(settings.UserName)
            || string.IsNullOrWhiteSpace(settings.Password)
            || string.IsNullOrWhiteSpace(settings.FromEmail))
        {
            throw new InvalidOperationException("SMTP configuration is incomplete.");
        }
    }
}

public sealed class OrderConfirmationEmail
{
    public string RecipientEmail { get; init; } = string.Empty;

    public string RecipientName { get; init; } = string.Empty;

    public int OrderId { get; init; }

    public DateTime CreatedAt { get; init; }

    public string StatusLabel { get; init; } = string.Empty;

    public FulfillmentMethod FulfillmentMethod { get; init; }

    public string StoreName { get; init; } = string.Empty;

    public string StoreAddress { get; init; } = string.Empty;

    public string StorePhone { get; init; } = string.Empty;

    public string RecipientPhone { get; init; } = string.Empty;

    public string? DeliveryAddress { get; init; }

    public string? OrderNote { get; init; }

    public PaymentMethod PaymentMethod { get; init; }

    public IReadOnlyList<OrderConfirmationEmailItem> Items { get; init; } = [];

    public decimal Subtotal { get; init; }

    public decimal DeliveryFee { get; init; }

    public decimal TotalAmount { get; init; }

    public string DetailsUrl { get; init; } = string.Empty;
}

public sealed class OrderStatusNotificationEmail
{
    public string RecipientEmail { get; init; } = string.Empty;

    public string RecipientName { get; init; } = string.Empty;

    public int OrderId { get; init; }

    public string StatusLabel { get; init; } = string.Empty;

    public string StatusMessage { get; init; } = string.Empty;

    public FulfillmentMethod FulfillmentMethod { get; init; }

    public string StoreName { get; init; } = string.Empty;

    public string StoreAddress { get; init; } = string.Empty;

    public string StorePhone { get; init; } = string.Empty;

    public string? DeliveryAddress { get; init; }

    public string DetailsUrl { get; init; } = string.Empty;
}

public sealed class OrderConfirmationEmailItem
{
    public string ProductName { get; init; } = string.Empty;

    public string SizeName { get; init; } = string.Empty;

    public int? SugarPercentage { get; init; }

    public int? IcePercentage { get; init; }

    public decimal UnitPrice { get; init; }

    public int Quantity { get; init; }

    public decimal LineTotal { get; init; }

    public IReadOnlyList<OrderConfirmationEmailTopping> Toppings { get; init; } = [];
}

public sealed class OrderConfirmationEmailTopping
{
    public string Name { get; init; } = string.Empty;

    public decimal UnitPrice { get; init; }
}
