using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZMSL.Shared.DTOs;

namespace ZMSL.App.Services
{
    /// <summary>
    /// 玩家论坛服务
    /// </summary>
    public class PlayerForumService
    {
        private readonly ApiService _apiService;

        public PlayerForumService(ApiService apiService)
        {
            _apiService = apiService;
        }

        /// <summary>
        /// 获取所有版块
        /// </summary>
        public async Task<List<ForumCategoryDto>> GetCategoriesAsync()
        {
            try
            {
                LogService.Instance.Debug("Getting categories...", "PlayerForumService");
                var response = await _apiService.GetForumCategoriesAsync();
                if (response.Success)
                {
                    LogService.Instance.Debug($"Got {(response.Data?.Count ?? 0)} categories.", "PlayerForumService");
                    return response.Data ?? new List<ForumCategoryDto>();
                }
                else
                {
                    LogService.Instance.Error($"Failed to get categories. Message: {response.Message}", "PlayerForumService");
                    return new List<ForumCategoryDto>();
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error("Exception getting categories", "PlayerForumService", ex);
                return new List<ForumCategoryDto>();
            }
        }

        /// <summary>
        /// 获取指定版块的帖子
        /// </summary>
        public async Task<PagedResult<ForumPostDto>> GetPostsAsync(int page = 1, int pageSize = 20, long? categoryId = null)
        {
            try
            {
                LogService.Instance.Debug($"Getting posts. Page: {page}, Size: {pageSize}, Category: {categoryId}", "PlayerForumService");
                var response = await _apiService.GetForumPostsAsync(page, pageSize, categoryId);
                if (response.Success)
                {
                    LogService.Instance.Debug($"Got {(response.Data?.Items?.Count ?? 0)} posts. Total: {response.Data?.TotalCount}", "PlayerForumService");
                    return response.Data ?? new PagedResult<ForumPostDto>();
                }
                else
                {
                    LogService.Instance.Error($"Failed to get posts. Message: {response.Message}", "PlayerForumService");
                    return new PagedResult<ForumPostDto>();
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error("Exception getting posts", "PlayerForumService", ex);
                return new PagedResult<ForumPostDto>();
            }
        }

        /// <summary>
        /// 搜索帖子
        /// </summary>
        public async Task<PagedResult<ForumPostDto>> SearchPostsAsync(string keyword, int page = 1, int pageSize = 20)
        {
            var response = await _apiService.SearchForumPostsAsync(keyword, page, pageSize);
            return response.Success ? response.Data ?? new PagedResult<ForumPostDto>() : new PagedResult<ForumPostDto>();
        }

        /// <summary>
        /// 获取帖子详情
        /// </summary>
        public async Task<ForumPostDto?> GetPostDetailAsync(long postId)
        {
            var response = await _apiService.GetForumPostAsync(postId);
            return response.Success ? response.Data : null;
        }

        /// <summary>
        /// 获取帖子评论
        /// </summary>
        public async Task<PagedResult<ForumCommentDto>> GetCommentsAsync(long postId, int page = 1, int pageSize = 20)
        {
            var response = await _apiService.GetForumCommentsAsync(postId, page, pageSize);
            return response.Success ? response.Data ?? new PagedResult<ForumCommentDto>() : new PagedResult<ForumCommentDto>();
        }

        /// <summary>
        /// 创建新帖子
        /// </summary>
        public async Task<ForumPostDto?> CreatePostAsync(CreatePostRequest request)
        {
            var response = await _apiService.CreateForumPostAsync(request);
            if (response.Success && response.Data != null)
            {
                OnThreadCreated?.Invoke(this, new ThreadEventArgs { Thread = response.Data });
                return response.Data;
            }
            throw new Exception(response.Message ?? "发布失败");
        }

        /// <summary>
        /// 更新帖子
        /// </summary>
        public async Task<ForumPostDto?> UpdatePostAsync(long id, CreatePostRequest request)
        {
            var response = await _apiService.UpdateForumPostAsync(id, request);
            if (response.Success)
            {
                return response.Data;
            }
            throw new Exception(response.Message ?? "更新失败");
        }

        /// <summary>
        /// 删除帖子
        /// </summary>
        public async Task<bool> DeletePostAsync(long id)
        {
            var response = await _apiService.DeleteForumPostAsync(id);
            if (response.Success)
            {
                OnThreadDeleted?.Invoke(this, new ThreadEventArgs { Thread = new ForumPostDto { Id = id } });
                return true;
            }
            return false;
        }

        /// <summary>
        /// 发表评论
        /// </summary>
        public async Task<ForumCommentDto?> CreateCommentAsync(CreateCommentRequest request)
        {
            var response = await _apiService.CreateForumCommentAsync(request);
            if (response.Success && response.Data != null)
            {
                OnPostCreated?.Invoke(this, new PostEventArgs { Comment = response.Data });
                return response.Data;
            }
            throw new Exception(response.Message ?? "发表评论失败");
        }

        /// <summary>
        /// 删除评论
        /// </summary>
        public async Task<bool> DeleteCommentAsync(long id)
        {
            var response = await _apiService.DeleteForumCommentAsync(id);
            return response.Success;
        }

        /// <summary>
        /// 点赞帖子
        /// </summary>
        public async Task<bool> LikePostAsync(long id)
        {
            var response = await _apiService.LikeForumPostAsync(id);
            return response.Success && response.Data != null && response.Data.GetValueOrDefault("isLiked");
        }

        /// <summary>
        /// 收藏帖子
        /// </summary>
        public async Task<bool> FavoritePostAsync(long id)
        {
            var response = await _apiService.FavoriteForumPostAsync(id);
            return response.Success && response.Data != null && response.Data.GetValueOrDefault("isFavorited");
        }

        /// <summary>
        /// 点赞评论
        /// </summary>
        public async Task<bool> LikeCommentAsync(long id)
        {
            var response = await _apiService.LikeForumCommentAsync(id);
            return response.Success && response.Data != null && response.Data.GetValueOrDefault("isLiked");
        }

        /// <summary>
        /// 获取我的帖子
        /// </summary>
        public async Task<PagedResult<ForumPostDto>> GetMyPostsAsync(int page = 1, int pageSize = 20)
        {
            var response = await _apiService.GetMyForumPostsAsync(page, pageSize);
            return response.Success ? response.Data ?? new PagedResult<ForumPostDto>() : new PagedResult<ForumPostDto>();
        }

        /// <summary>
        /// 获取我的收藏
        /// </summary>
        public async Task<PagedResult<ForumPostDto>> GetMyFavoritesAsync(int page = 1, int pageSize = 20)
        {
            var response = await _apiService.GetMyForumFavoritesAsync(page, pageSize);
            return response.Success ? response.Data ?? new PagedResult<ForumPostDto>() : new PagedResult<ForumPostDto>();
        }

        /// <summary>
        /// 获取我的评论
        /// </summary>
        public async Task<PagedResult<ForumCommentDto>> GetMyCommentsAsync(int page = 1, int pageSize = 20)
        {
            var response = await _apiService.GetMyForumCommentsAsync(page, pageSize);
            return response.Success ? response.Data ?? new PagedResult<ForumCommentDto>() : new PagedResult<ForumCommentDto>();
        }

        /// <summary>
        /// 获取通知
        /// </summary>
        public async Task<PagedResult<NotificationDto>> GetNotificationsAsync(int page = 1, int pageSize = 20)
        {
            LogService.Instance.Debug($"Calling GetNotificationsAsync Page={page}, Size={pageSize}", "PlayerForumService");
            var response = await _apiService.GetNotificationsAsync(page, pageSize);
            if (!response.Success)
            {
                LogService.Instance.Error($"Failed to get notifications: {response.Message}", "PlayerForumService");
                return new PagedResult<NotificationDto>();
            }
            LogService.Instance.Debug($"Got notifications: Count={response.Data?.Items?.Count ?? 0}", "PlayerForumService");
            return response.Data ?? new PagedResult<NotificationDto>();
        }

        /// <summary>
        /// 获取未读通知数量
        /// </summary>
        public async Task<long> GetUnreadNotificationCountAsync()
        {
            var response = await _apiService.GetUnreadNotificationCountAsync();
            return response.Success ? response.Data : 0;
        }

        /// <summary>
        /// 标记所有通知为已读
        /// </summary>
        public async Task<bool> MarkAllNotificationsAsReadAsync()
        {
            var response = await _apiService.MarkAllNotificationsAsReadAsync();
            return response.Success;
        }

        #region 事件

        public event EventHandler<ThreadEventArgs>? OnThreadCreated;
        public event EventHandler<PostEventArgs>? OnPostCreated;
        public event EventHandler<ThreadEventArgs>? OnThreadDeleted;

        #endregion
    }

    #region 事件参数

    public class ThreadEventArgs : EventArgs
    {
        public ForumPostDto Thread { get; set; } = new();
    }

    public class PostEventArgs : EventArgs
    {
        public ForumCommentDto Comment { get; set; } = new();
    }

    #endregion
}
