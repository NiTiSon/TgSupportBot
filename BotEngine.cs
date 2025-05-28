using Serilog;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TgSupportBot.Extensions;

namespace TgSupportBot;

public sealed class BotEngine
{
	/// <summary>
	/// Среднее время на загрузку изображения или видео в милисекундах.
	/// </summary>
	private const int AverageTimeBetweenMediaMessages = 700;
	private const int BriefMessageMaximumLength = 256;
	private const int DescriptionMessageMaximumLength = 1024;
	private const int LocationMessageMaximumLength = 512;

	private TelegramBotClient BotClient { get; }
	private User Me { get; set; } = null!;
	private UserStateController StateController { get; }

	public BotEngine(string token)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(token);
		BotClient = new TelegramBotClient(token);
		StateController = new UserStateController(BotClient);
	}
	
	public async Task Start()
	{
		using CancellationTokenSource cts = new();

		ReceiverOptions receiverOptions = new()
		{
			AllowedUpdates = [
				UpdateType.Message,
				UpdateType.ChatMember,
				UpdateType.CallbackQuery,
			],
			DropPendingUpdates = true,
		};
		BotClient.StartReceiving(
			HandleUpdateAsync,
			HandlePollingErrorAsync,
			receiverOptions,
			cts.Token
		);
		
		Me = await BotClient.GetMe(cts.Token);
		Log.Information("Bot started! @{BotName}", Me.Username);
		await Task.Delay(Timeout.Infinite, cts.Token);
	}

	private async Task HandleNewMemberAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken = default)
	{
		if (update.ChatMember!.NewChatMember is not ChatMemberMember) return;

		await botClient.SendMessage(
			update.ChatMember!.Chat,
			Localization.WelcomeMessage.Format(update.ChatMember.NewChatMember.User.Username),
			cancellationToken: cancellationToken);
	}

	private async Task HandleUpdateAsync(ITelegramBotClient botClient,
		Update update, CancellationToken cancellationToken = default)
	{
		if (update.Type == UpdateType.ChatMember)
		{
			await HandleNewMemberAsync(botClient, update, cancellationToken);
		}

		if (update.Message?.Chat.Type == ChatType.Supergroup)
		{
			await botClient.SetMyCommands([
				new BotCommand("/report", Localization.ReportCommandDescription)
			], scope: BotCommandScope.AllGroupChats(), cancellationToken: cancellationToken);
		}
		if (update.Message is { } message)
		{
			await HandleMessageAsync(botClient, update, message, cancellationToken);
		}
		if (update.CallbackQuery is { } callbackQuery)
		{
			await HandleCallbackQueryAsync(botClient, callbackQuery, update, cancellationToken);
		}
	}

	private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, Update update, CancellationToken cancellationToken)
	{
		UserState state = StateController.GetCurrentStateOrCreateNew(callbackQuery.From);
		
		if (state.Step == UserStateStep.RequireAttachments)
		{
			long chatId = state.AffectedMessages.Last().Chat.Id;
			int? threadId = state.AffectedMessages.LastOrDefault()?.MessageThreadId;
			if (callbackQuery.Data == Localization.ReportSkipAttachments
				|| callbackQuery.Data == Localization.ReportCreateButton)
			{
				await botClient.SendMessage(chatId,
					Localization.RequestMessage_4.Format(state.Brief,
						state.User.Username,
						state.Description,
						state.Location ?? "Не указано"),
					messageThreadId: threadId,
					cancellationToken: cancellationToken);

				await SendAldumFromState(chatId, threadId, state, cancellationToken);

				if (DateTime.Now.Hour is >= 18 or < 9)
				{
					await botClient.SendMessage(chatId,
						Localization.WorkTimeWarningMessage,
						messageThreadId: threadId,
						cancellationToken: cancellationToken);
				}

				await StateController.Release(callbackQuery.From, cancellationToken);
			}
		}
	}

	private async Task SendAldumFromState(long chatId, int? threadId, UserState state, CancellationToken cancellationToken = default)
	{
		switch (state.Files.Count)
		{
			case 0:
				return;
			default:
				List<IAlbumInputMedia> album = new(capacity: 10);


				foreach (FileBase file in state.Files)
				{
					if (file is PhotoSize photo)
					{
						album.Add(new InputMediaPhoto(photo));
					}
					else if (file is Video video)
					{
						album.Add(new InputMediaVideo(video));
					}
				}

				while (album.Count > 0)
				{
					await BotClient.SendMediaGroup(
						chatId,
						album.Take(10),
						messageThreadId: threadId,
						disableNotification: true,
						cancellationToken: cancellationToken
					);

					album.RemoveRange(0, int.Min(10, album.Count));
				}


				break;
		}
	}

	private async Task HandleMessageAsync(ITelegramBotClient botClient, Update update, Message message, CancellationToken cancellationToken)
	{
		if ((message.Text == "/start" || message.Text == "/start@" + Me.Username) && message.Chat.Type == ChatType.Private )
		{
			await botClient.SendMessage(message.Chat.Id,$"Данный бот не работает в личном чате, чтобы составить заявку: перейдите в группу.", cancellationToken: cancellationToken);
		}

		if ((message.Text == "/report" || message.Text == "/report@" + Me.Username) &&
			message.Chat.Type == ChatType.Supergroup)
		{
			UserState state = StateController.GetCurrentStateOrCreateNew(message.From!);
			Message botMessage = await botClient.SendMessage(message.Chat.Id, "Опишите свою проблему (вкратце):",
				messageThreadId: message.MessageThreadId, cancellationToken: cancellationToken);
			state.AffectedMessages.Add(botMessage);
			state.AffectedMessages.Add(message);
			state.Step = UserStateStep.RequireBrief;
		}
		else if (message.Chat.Type == ChatType.Supergroup)
		{
			UserState state = StateController.GetCurrentStateOrCreateNew(message.From!);

			Message botMessage;
			switch (state.Step)
			{
				case UserStateStep.None:
					return;

				case UserStateStep.RequireBrief:
					if (!await RequireMaximumLength(state, message, BriefMessageMaximumLength, cancellationToken)) return;
					state.Brief = message.Text!;

					if (!await RequireOnlyText(state, message, cancellationToken)) return;
					botMessage = await botClient.SendMessage(message.Chat.Id,
						Localization.RequireDescriptionMessage,
						messageThreadId: message.MessageThreadId,
						cancellationToken: cancellationToken);

					state.AffectedMessages.Add(message);
					state.AffectedMessages.Add(botMessage);
					state.Step = UserStateStep.RequireDescription;
					break;
				case UserStateStep.RequireDescription:
					if (!await RequireMaximumLength(state, message, DescriptionMessageMaximumLength, cancellationToken)) return;
					state.Description = message.Text!;

					if (!await RequireOnlyText(state, message, cancellationToken)) return;
					botMessage = await botClient.SendMessage(message.Chat.Id,
						Localization.RequireLocationMessage,
						messageThreadId: message.MessageThreadId,
						cancellationToken: cancellationToken);

					state.AffectedMessages.Add(message);
					state.AffectedMessages.Add(botMessage);
					state.Step = UserStateStep.RequireLocation;
					break;
				case UserStateStep.RequireLocation:
					if (!await RequireMaximumLength(state, message, LocationMessageMaximumLength, cancellationToken)) return;
					state.Location = message.Text!;

					if (!await RequireOnlyText(state, message, cancellationToken)) return;
					InlineKeyboardMarkup keyboard = new(InlineKeyboardButton.WithCallbackData(Localization.ReportSkipAttachments));
					botMessage = await botClient.SendMessage(message.Chat.Id,
						Localization.RequireAttachmentMessage,
						replyMarkup: keyboard,
						messageThreadId: message.MessageThreadId,
						cancellationToken: cancellationToken);

					state.AffectedMessages.Add(message);
					state.AffectedMessages.Add(botMessage);
					state.Step = UserStateStep.RequireAttachments;
					break;
				case UserStateStep.RequireAttachments:
					if (message.Photo is not null || message.Video is not null) // Добавить фото или видео при отправки пользователем
					{
						if (message.Photo is not null)
						{
							state.Files.Add(message.Photo[^1]);
						}
						else if (message.Video is not null)
						{
							state.Files.Add(message.Video);
						}
						state.AffectedMessages.Add(message);

						await Task.Delay(AverageTimeBetweenMediaMessages, cancellationToken);

						if (state.AffectedMessages.LastOrDefault() != message)
							return;

						await StateController.RemoveLastMediaMessage(message.From!, cancellationToken);

						InlineKeyboardMarkup inlineKeyboard = new(InlineKeyboardButton.WithCallbackData(Localization.ReportCreateButton));

						int imageCount = state.Files.OfType<PhotoSize>().Count();
						botMessage = state.LastMediaMessage = await botClient.SendMessage(
							message.Chat.Id,
							Localization.PresentedMediaMessage_3.Format(
								imageCount,
								GetLocalizationForImage(imageCount),
								state.Files.OfType<Video>().Count()),
							replyMarkup: inlineKeyboard,
							messageThreadId: message.MessageThreadId,
							cancellationToken: cancellationToken);

						state.AffectedMessages.Add(botMessage);
					}
					break;
				default:
					Log.Warning("Invalid state is presented");
					state.Step = UserStateStep.None;
					break;
			}
		}
	}

	private async Task<bool> RequireMaximumLength(UserState state, Message message, int maximum, CancellationToken cancellationToken = default)
	{
		if (message.Text?.Length > maximum)
		{
			state.AffectedMessages.Add(await BotClient.SendMessage(message.Chat, Localization.MessageMaxLimitMessage_1.Format(maximum), messageThreadId: message.MessageThreadId, cancellationToken: cancellationToken));
			return false;
		}

		return true;
	}

	private async Task<bool> RequireOnlyText(UserState state, Message message, CancellationToken cancellationToken = default)
	{
		if (message.Photo != null
		 || message.Video != null)
		{
			state.AffectedMessages.Add(message);
			state.AffectedMessages.Add(
				await BotClient.SendMessage(message.Chat.Id,
					Localization.MessageOnlyTextLimit,
					messageThreadId : message.MessageThreadId,
					cancellationToken: cancellationToken
				)
			);

			return false;
		}

		return true;
	}

	private static Task HandlePollingErrorAsync(ITelegramBotClient botClient,
		Exception exception, CancellationToken cancellationToken)
	{
		string errorMessage = exception switch
		{
			ApiRequestException apiRequestException
				=> $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
			_ => exception.ToString()
		};

		Log.Error(errorMessage);
		return Task.CompletedTask;
	}

	private static string GetLocalizationForImage(int quanity)
	{
		if (quanity == 1) return Localization.ImageOne;

		unchecked
		{
			int end = quanity % 10;

			return end switch
			{
				1 or 2 or 3 or 4 => Localization.ImageFew,
				_ => Localization.ImageMany
			};
		}
	}
}