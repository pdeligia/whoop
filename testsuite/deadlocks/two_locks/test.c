//xfail:DRIVER_ERROR
//

#include <linux/device.h>
#include <whoop.h>

struct shared {
	int resource;
	struct mutex mutex;
	struct mutex mutex2;
};

static void entrypoint(struct test_device *dev)
{
	struct shared *tp = testdev_priv(dev);
	
	mutex_lock(&tp->mutex);
	mutex_lock(&tp->mutex2);
	tp->resource = 1;
}

static int init(struct pci_dev *pdev, const struct pci_device_id *ent)
{
	struct shared *tp;
	struct test_device *dev = alloc_testdev(sizeof(*tp));
	
	tp = testdev_priv(dev);
	mutex_init(&tp->mutex);
	mutex_init(&tp->mutex2);
	
	entrypoint(dev);
	
	return 0;
}

static struct test_driver test = {
	.probe = init,
	.ep1 = entrypoint
};
