namespace ChatCRM.Application.Chats.DTOs
{
    public class ChatsPageViewModel
    {
        public List<InstanceDto> Instances { get; set; } = new();
        public int? ActiveInstanceId { get; set; }
        public List<ConversationDto> Conversations { get; set; } = new();
    }
}
