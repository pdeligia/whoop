#ifndef _LINUX_WORKQUEUE_H
#define _LINUX_WORKQUEUE_H

#include <linux/timer.h>
#include <linux/atomic.h>

struct work_struct {
    void (*func)(void *);
    void *data;
};

#define DECLARE_WORK(_work, _func, _data) \
	struct work_struct _work = { \
           .func = (_func), \
           .data = (_data), \
        }

#define PREPARE_WORK(_work, _func, _data) \
	do { \
		(_work)->func = _func; \
		(_work)->data = _data; \
	} while (0)

#define INIT_WORK(_work, _func, _data) \
	do { \
		PREPARE_WORK((_work), (_func), (_data)); \
	} while (0)

#define INIT_WORK(_work, _func)						\
	do {								\
		PREPARE_WORK((_work), (_func), 0);			\
	} while (0)

bool schedule_work(struct work_struct *work);
void flush_scheduled_work(void);
bool cancel_work_sync(struct work_struct *work);

#endif